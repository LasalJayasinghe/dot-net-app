using System.Security.Claims;
using dotnetApp.Application.Dtos;
using dotnetApp.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

/// <summary>
/// REST API for multi-portfolio management.
///
/// Endpoints:
///   GET    /api/portfolios                       — List user's portfolios (optionally filtered by type)
///   POST   /api/portfolios                       — Create a new portfolio
///   GET    /api/portfolios/{id}                  — Get portfolio details + holdings with live valuation
///   PUT    /api/portfolios/{id}                  — Update portfolio name / description
///   DELETE /api/portfolios/{id}                  — Delete a portfolio
///   POST   /api/portfolios/{id}/holdings         — Add or update a holding (weighted average for re-buys)
///   PUT    /api/portfolios/{id}/holdings/{hId}   — Correct a holding's quantity / average price
///   DELETE /api/portfolios/{id}/holdings/{hId}   — Remove a specific holding
///   POST   /api/portfolios/{id}/trades           — Buy / sell (moves cash, records realised P/L)
///   POST   /api/portfolios/{id}/cash             — Deposit / withdraw / set the cash balance
///   GET    /api/portfolios/{id}/transactions     — The portfolio's ledger, newest first
///   GET    /api/portfolios/net-worth             — Aggregated net worth across all portfolios in display currency
///   GET    /api/portfolios/history               — Hourly value-over-time series (net worth, or one portfolio)
///   GET    /api/portfolios/{id}/insights         — Signals per holding + upcoming dividends (stocks)
///   POST   /api/portfolios/{id}/sync-file        — Sync portfolio holdings from a broker PDF/Excel file
/// </summary>
[ApiController]
[Route("api/portfolios")]
[Authorize]
public class PortfolioApiController : ControllerBase
{
    private readonly PortfolioService _portfolioService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PortfolioApiController> _logger;
    private readonly IPortfolioFileSyncService _fileSyncService;
    private readonly PortfolioSnapshotService _snapshotService;
    private readonly PortfolioInsightsService _insightsService;

    public PortfolioApiController(
        PortfolioService portfolioService,
        UserManager<ApplicationUser> userManager,
        ILogger<PortfolioApiController> logger,
        IPortfolioFileSyncService fileSyncService,
        PortfolioSnapshotService snapshotService,
        PortfolioInsightsService insightsService)
    {
        _insightsService  = insightsService;
        _portfolioService = portfolioService;
        _userManager      = userManager;
        _logger           = logger;
        _fileSyncService   = fileSyncService;
        _snapshotService  = snapshotService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/portfolios
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>List all portfolios for the current user.</summary>
    /// <param name="type">Optional filter: "Stocks" or "Crypto"</param>
    [HttpGet]
    public async Task<IActionResult> GetPortfolios([FromQuery] string? type = null)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        PortfolioType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<PortfolioType>(type, ignoreCase: true, out var parsed))
            typeFilter = parsed;

        var portfolios = await _portfolioService.GetUserPortfoliosAsync(userId, typeFilter);
        return Ok(portfolios);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/portfolios/net-worth
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Aggregated net worth across all portfolios, converted to display currency.</summary>
    /// <param name="currency">Target display currency: "LKR" (default) or "USDT"</param>
    [HttpGet("net-worth")]
    public async Task<IActionResult> GetNetWorth()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var data = await _portfolioService.GetNetWorthAsync(userId);
        return Ok(data);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/portfolios/history
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Portfolio value over time, from the hourly snapshots written by PortfolioSnapshotJob.
    /// </summary>
    /// <param name="range">1D, 1W, 1M (default), 3M, 1Y or ALL. 3M and longer are down-sampled to daily points.</param>
    /// <param name="portfolioId">Omit for aggregated net worth; supply an id for a single portfolio.</param>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string range = "1M",
        [FromQuery] int? portfolioId = null,
        CancellationToken ct = default)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (portfolioId.HasValue && !await _portfolioService.OwnsPortfolioAsync(userId, portfolioId.Value))
            return NotFound(new { message = "Portfolio not found." });

        var history = await _snapshotService.GetHistoryAsync(userId, portfolioId, range, ct);
        return Ok(history);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/portfolios
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Create a new portfolio.</summary>
    [HttpPost]
    public async Task<IActionResult> CreatePortfolio([FromBody] CreatePortfolioRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var validCurrencies = new[] { "LKR", "USDT", "USD" };
        if (!validCurrencies.Contains(req.BaseCurrency.ToUpperInvariant()))
            return BadRequest(new { message = $"BaseCurrency must be one of: {string.Join(", ", validCurrencies)}" });

        var portfolio = await _portfolioService.CreatePortfolioAsync(userId, req);
        return CreatedAtAction(nameof(GetPortfolioById), new { id = portfolio.Id }, portfolio);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/portfolios/{id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Get a specific portfolio with all holdings and live valuations.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPortfolioById(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var detail = await _portfolioService.GetPortfolioDetailAsync(userId, id);
        return detail == null ? NotFound(new { message = "Portfolio not found." }) : Ok(detail);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/portfolios/{id}/insights
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rule-based signals per holding (trend, RSI, 52-week range, volume spike) from real CSE daily prices,
    /// plus announced dividends for the stocks held. Loaded separately from the detail (slower: price history).
    /// </summary>
    [HttpGet("{id:int}/insights")]
    public async Task<IActionResult> GetInsights(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var insights = await _insightsService.GetInsightsAsync(userId, id, HttpContext.RequestAborted);
        return insights == null ? NotFound(new { message = "Portfolio not found." }) : Ok(insights);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/portfolios/{id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Update portfolio name or description.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePortfolio(int id, [FromBody] UpdatePortfolioRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var updated = await _portfolioService.UpdatePortfolioAsync(userId, id, req);
        return updated ? NoContent() : NotFound(new { message = "Portfolio not found." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/portfolios/{id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Delete a portfolio and all its holdings.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePortfolio(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var deleted = await _portfolioService.DeletePortfolioAsync(userId, id);
        return deleted ? NoContent() : NotFound(new { message = "Portfolio not found." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/portfolios/{id}/holdings
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a new holding to a portfolio, or update an existing one using a
    /// weighted average buy price (simulates re-buying).
    /// </summary>
    [HttpPost("{id:int}/holdings")]
    public async Task<IActionResult> AddHolding(int id, [FromBody] AddHoldingRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (req.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than zero." });
        if (req.AverageBuyPrice < 0)
            return BadRequest(new { message = "AverageBuyPrice cannot be negative." });

        var (success, message, dto) = await _portfolioService.AddOrUpdateHoldingAsync(userId, id, req);
        return success ? Ok(dto) : NotFound(new { message });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Trades, cash, holding corrections and the ledger
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Buy or sell. Buys pay from cash unless UseCash is false (recording a position you already had);
    /// sells add the net proceeds to cash and record realised P/L. Returns the updated portfolio.
    /// </summary>
    [HttpPost("{id:int}/trades")]
    public async Task<IActionResult> Trade(int id, [FromBody] TradeRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Symbol)) return BadRequest(new { message = "Symbol is required." });
        if (req.Quantity <= 0) return BadRequest(new { message = "Quantity must be greater than zero." });
        if (req.Price <= 0) return BadRequest(new { message = "Price must be greater than zero." });
        if (req.Fees < 0) return BadRequest(new { message = "Fees can't be negative." });

        var (success, message, detail) = await _portfolioService.TradeAsync(userId, id, req);
        return success ? Ok(detail) : BadRequest(new { message });
    }

    /// <summary>Deposit, withdraw, or set the cash balance ("Set" corrects it to match the broker).</summary>
    [HttpPost("{id:int}/cash")]
    public async Task<IActionResult> Cash(int id, [FromBody] CashRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var (success, message, detail) = await _portfolioService.ChangeCashAsync(userId, id, req);
        return success ? Ok(detail) : BadRequest(new { message });
    }

    /// <summary>Correct a holding's quantity / average price. No cash moves; the old values stay in the ledger.</summary>
    [HttpPut("{id:int}/holdings/{hId:int}")]
    public async Task<IActionResult> UpdateHolding(int id, int hId, [FromBody] UpdateHoldingRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (req.Quantity <= 0) return BadRequest(new { message = "Quantity must be greater than zero (remove the holding instead)." });
        if (req.AverageBuyPrice < 0) return BadRequest(new { message = "Average buy price can't be negative." });

        var (success, message, detail) = await _portfolioService.UpdateHoldingAsync(userId, id, hId, req);
        return success ? Ok(detail) : NotFound(new { message });
    }

    /// <summary>The portfolio's ledger, newest first.</summary>
    [HttpGet("{id:int}/transactions")]
    public async Task<IActionResult> Transactions(int id, [FromQuery] int limit = 100)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var items = await _portfolioService.GetTransactionsAsync(userId, id, limit);
        return items == null ? NotFound(new { message = "Portfolio not found." }) : Ok(items);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/portfolios/{id}/holdings/{hId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Remove a specific holding from a portfolio.</summary>
    [HttpDelete("{id:int}/holdings/{hId:int}")]
    public async Task<IActionResult> DeleteHolding(int id, int hId)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        var deleted = await _portfolioService.DeleteHoldingAsync(userId, id, hId);
        return deleted ? NoContent() : NotFound(new { message = "Holding not found." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/portfolios/{id}/sync-file
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/sync-file")]
    public async Task<IActionResult> SyncFile(int id, IFormFile file)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("File size exceeds 5MB limit.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".xlsx" && ext != ".xls")
            return BadRequest("Only PDF and Excel files are allowed.");

        try
        {
            using var stream = file.OpenReadStream();
            var parsedHoldings = new List<ParsedHoldingDto>();

            if (ext == ".pdf")
            {
                parsedHoldings = _fileSyncService.ParsePdf(stream);
            }
            else
            {
                parsedHoldings = _fileSyncService.ParseExcel(stream);
            }

            if (!parsedHoldings.Any())
            {
                return BadRequest("No valid holdings were extracted from the file. Ensure it is an ATrad Online Client Portfolio report.");
            }

            await _portfolioService.SyncHoldingsAsync(id, userId, parsedHoldings);
            return Ok(new { Count = parsedHoldings.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing file for portfolio {Id}", id);
            return StatusCode(500, $"An error occurred while parsing the file: {ex.Message}");
        }
    }
}
