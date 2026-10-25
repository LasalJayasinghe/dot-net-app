using dotnetApp;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using dotnetApp.Infrastructure.Data;
using dotnetApp.Infrastructure.Data.Seeders;
using dotnetApp.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Linq;
using System.Security.Claims;
using System.Threading.RateLimiting;
using dotnetApp.Application.Interface;
using dotnetApp.Application.Services;
using dotnetApp.Application.Services.NewsProviders;
using dotnetApp.Jobs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // The file sink writes inside the container, where nothing collects it - stdout is the only
    // place a hosted environment (Render/Docker) actually picks logs up from.
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();


// Configuration lives in .env, not appsettings.json, so that secrets stay out of the repo and
// each environment (dev laptop, Contabo host, container) supplies its own values. Loaded before
// CreateBuilder so the values are already in the process environment when the default
// EnvironmentVariables provider reads them - that provider maps "__" to ":", so Jwt__Key in the
// file arrives as Jwt:Key in IConfiguration and overrides anything left in appsettings.json.
// onlyExactPath:false walks up parent directories, so running from bin/ still finds the file.
DotNetEnv.Env.Load(options: new DotNetEnv.LoadOptions(
    setEnvVars: true,
    // A real environment variable (systemd unit, docker -e, CI secret) must beat the file,
    // so the same image can be pointed at staging or production without editing .env.
    clobberExistingVars: false,
    onlyExactPath: false));

var builder = WebApplication.CreateBuilder(args);

// Required for ExcelDataReader
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");

    options.UseMySql(
        cs,
        ServerVersion.AutoDetect(cs)
    );
});

// Use Serilog instead of default logging
builder.Host.UseSerilog();

// Memory Cache
builder.Services.AddMemoryCache();

// Add services to the container.
builder.Services.AddControllers();

// SignalR — required for real-time Crypto Dashboard push
builder.Services.AddSignalR();
builder.Services.AddHttpClient<StockService>(client =>
{
    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Accept", "application/json, text/plain, */*");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Origin", "https://www.cse.lk");
});

builder.Services.AddScoped<StockRepository>();
builder.Services.AddScoped<AlertRepository>();
builder.Services.AddScoped<ProfileRepository>();
// builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IPortfolioFileSyncService, PortfolioFileSyncService>();

builder.Services.AddHttpClient<TelegramService>();
builder.Services.AddHttpClient<IPushNotificationService, PushNotificationService>();
builder.Services.AddHttpClient<IBrevoEmailService, BrevoEmailService>();

builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<IStrategy, EmaRsiStrategy>();
builder.Services.AddSingleton<TradingBotService>();

// Crypto Dashboard services
builder.Services.AddScoped<ICryptoMarketService, CryptoMarketService>();
builder.Services.AddScoped<IAiMarketSummaryService, AiMarketSummaryService>();
builder.Services.AddScoped<dotnetApp.Application.Services.PortfolioService>();

// Hourly portfolio value snapshots (powers the portfolio-value-over-time graph)
builder.Services.AddScoped<dotnetApp.Application.Services.PortfolioSnapshotService>();
builder.Services.AddHostedService<PortfolioSnapshotJob>();

// AI Chat Integration
builder.Services.AddHttpClient<AiAgentService>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    var timeoutSeconds = builder.Configuration.GetValue<int?>("Ollama:TimeoutSeconds") ?? 300;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetNetWorthTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetStockDataTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetBinanceTickerTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetMarketStatusTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetTopGainersTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetTopLosersTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetProfileTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetPortfolioHoldingsTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetMarketNewsTool>();

// News Ingestion Multi-Provider Pipeline
builder.Services.AddHttpClient<WorldNewsApiProvider>();
builder.Services.AddHttpClient<RssFeedNewsProvider>();
builder.Services.AddTransient<INewsProvider, WorldNewsApiProvider>();
builder.Services.AddTransient<INewsProvider, RssFeedNewsProvider>();
builder.Services.AddScoped<NewsIngestionService>();
builder.Services.AddHostedService<NewsIngestionJob>();

// Async Background News Validation Pipeline (Ollama LLM)
builder.Services.AddHttpClient<NewsValidationService>();
builder.Services.AddScoped<NewsValidationService>();
builder.Services.AddHostedService<NewsValidationJob>();

// Binance P2P Rate Sync Service & Job
builder.Services.AddHttpClient<BinanceP2pService>();
builder.Services.AddHostedService<ExchangeRateJob>();

// Named HttpClient for Binance REST API — no API keys needed for public endpoints
builder.Services.AddHttpClient("binance", client =>
{
    client.BaseAddress = new Uri("https://api.binance.com");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceService>());
builder.Services.AddHostedService<AlertJob>();
builder.Services.AddHostedService<CseDividendJob>();

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<PushSettings>(builder.Configuration.GetSection("Push"));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Lock an account after repeated wrong passwords, so it cannot be guessed indefinitely.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardAccess", policy =>
        policy.RequireClaim("permission", "dashboard.access"));

    options.AddPolicy("AlertCreate", policy =>
        policy.RequireClaim("permission", "alert.create"));

    options.AddPolicy("AlertEdit", policy =>
        policy.RequireClaim("permission", "alert.edit"));

    options.AddPolicy("AlertView", policy =>
        policy.RequireClaim("permission", "alert.view"));
});

// Cookie related authentication
// builder.Services.ConfigureApplicationCookie(options =>
// {
//     options.LoginPath = "/Auth/Login";
//     options.AccessDeniedPath = "/Home/AccessDenied";
// });

// The signing key is required. Falling back to a literal baked into the source would mean a
// misconfigured environment silently signs and validates tokens with a publicly known key,
// letting anyone forge a token for any user - so fail fast at startup instead.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it via user-secrets (dotnet user-secrets set \"Jwt:Key\" \"<key>\") " +
        "or the Jwt__Key environment variable.");
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 bytes to sign HMAC-SHA256 tokens.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)
        ),

        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return Task.CompletedTask;
        }
    };
});

// Every endpoint was previously unthrottled, so a single caller could hammer database-backed
// routes (or the paid third-party ingestion routes) without limit. Partitioned per
// authenticated user, falling back to remote IP for anonymous callers.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey =
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

const string CorsPolicyName = "FrontendOrigins";

// Reflecting any origin while also allowing credentials defeats the browser rule that forbids
// "*" together with credentials: it lets any site on the internet make credentialed
// cross-origin calls to this API. Origins are allowlisted from configuration instead.
var allowedOrigins = (builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>())
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is not configured. List the exact front-end origins allowed to call " +
        "this API, e.g. \"Cors\": { \"AllowedOrigins\": [ \"http://localhost:3000\" ] }.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for SignalR cross-origin
    });
});

// Behind Cloudflare Tunnel every request reaches Kestrel from the cloudflared container, so without
// this the rate limiter would lump all anonymous callers into one shared bucket and logs would show a
// single IP. CF-Connecting-IP is set by Cloudflare's edge and overwrites anything the client sends.
// Only enable this when the API has no published port - if it were reachable directly, any caller
// could send the header and pick their own rate-limit partition.
var behindCloudflare = builder.Configuration.GetValue<bool>("ReverseProxy:Cloudflare");
if (behindCloudflare)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardedForHeaderName = "CF-Connecting-IP";
        // cloudflared's address on the Docker network is not fixed, so trust comes from the network
        // layout (nothing but cloudflared can reach the API) rather than an IP allowlist.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (cs != null)
{
    builder.Services.AddHealthChecks()
        .AddMySql(cs);
}

var app = builder.Build();

var binance = app.Services.GetRequiredService<BinanceService>();
var bot = app.Services.GetRequiredService<TradingBotService>();

binance.CandleClosed += async (c) => await bot.OnCandleClosed(c);

// First in the pipeline so logging, HSTS, HTTPS redirection and rate limiting all see the real
// client IP and the original https scheme.
if (behindCloudflare)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        if (db.Database.CanConnect())
        {
            Console.WriteLine("✅ Database connection SUCCESS");
        }
        else
        {
            Console.WriteLine("❌ Database connection FAILED");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ Database ERROR: " + ex.Message);
    }

    await RoleSeeder.SeedRolesAndPermissionsAsync(scope.ServiceProvider);
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
// Runs after authentication so limits can be partitioned per user, but before authorization so
// that unauthenticated floods are throttled too rather than short-circuiting on a 401.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Map("/error", (HttpContext http) =>
{
    // UseExceptionHandler re-executes into here; without pulling the exception off the feature
    // the original stack trace is lost and the caller just sees a bare 500.
    var handlerFeature = http.Features.Get<IExceptionHandlerFeature>();
    var pathFeature = http.Features.Get<IExceptionHandlerPathFeature>();

    if (handlerFeature?.Error is { } error)
    {
        Log.Error(
            error,
            "Unhandled exception on {Method} {Path}",
            pathFeature?.Endpoint?.DisplayName ?? http.Request.Method,
            pathFeature?.Path ?? http.Request.Path.Value);
    }
    else
    {
        Log.Error("Request failed on {Path} but no exception was captured", http.Request.Path.Value);
    }

    return Results.Problem();
});

// SignalR hub endpoint for the Crypto Trading Dashboard
app.MapHub<CryptoHub>("/hubs/crypto");

app.MapHealthChecks("/health");


app.Run();
