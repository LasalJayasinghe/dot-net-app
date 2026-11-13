using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using dotnetApp.Application.Services.Ai;
using dotnetApp.Application.Services.Ai.Tools;

namespace dotnetApp.Application.Services;

/// <summary>A previous turn of the conversation, sent by the client so the agent has context.</summary>
public sealed record ChatTurn(string Role, string Content);

/// <summary>One item of the chat stream: either a text chunk or a tool-activity update.</summary>
public sealed record AiStreamEvent(string Type, string? Text = null, string? ToolName = null, string? Status = null, string? Description = null)
{
    public static AiStreamEvent Chunk(string text) => new("text", Text: text);
    public static AiStreamEvent Tool(string name, string status, string description) =>
        new("tool", ToolName: name, Status: status, Description: description);
}

public class AiAgentService
{
    private const int MaxHistoryTurns = 12;
    private const int MaxHistoryChars = 4000;

    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IEnumerable<IMcpTool> _tools;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiAgentService> _logger;

    public AiAgentService(HttpClient httpClient, IEnumerable<IMcpTool> tools, IConfiguration configuration, ILogger<AiAgentService> logger)
    {
        _httpClient = httpClient;
        _tools = tools;
        _configuration = configuration;
        _logger = logger;
    }

    public async IAsyncEnumerable<AiStreamEvent> StreamChatAsync(
        string prompt,
        IReadOnlyList<ChatTurn>? history,
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are an expert financial data assistant for a stock and cryptocurrency dashboard.
You have access to real-time financial tools to query market data, user portfolios, stock prices, crypto tickers, user net worth, user profiles, and market gainers/losers.

CRITICAL INSTRUCTIONS:
1. You MUST ALWAYS use the provided tools whenever a question requires user portfolio details, stock prices, crypto tickers, net worth, market status, or user profile information.
2. NEVER guess, estimate, fabricate, or use internal knowledge for financial data.
3. If the user asks about their net worth, call `get_net_worth`.
4. If the user asks about their portfolio holdings or assets, call `get_portfolio_holdings`.
5. If the user asks about stock prices (e.g. COMB, SAMP), call `get_stock_price`.
6. If the user asks about crypto prices (e.g. BTCUSDT, ETHUSDT), call `get_binance_ticker`.
7. If the user asks a multi-part question (e.g. 'What is my net worth and what is COMB price?'), call ALL relevant tools needed to answer completely.
8. When tool results are returned, synthesize the data into a helpful, clear, and professional response for the user.
9. Earlier messages in the conversation are context only; always fetch fresh data with tools rather than reusing numbers from them.";

        var messages = new List<object> { new { role = "system", content = systemPrompt } };

        // Conversation memory: the client sends its recent turns (user/assistant only, trimmed).
        if (history is { Count: > 0 })
        {
            foreach (var turn in history.TakeLast(MaxHistoryTurns))
            {
                var role = turn.Role?.ToLowerInvariant();
                if (role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Content)) continue;
                var text = turn.Content.Length > MaxHistoryChars ? turn.Content[..MaxHistoryChars] : turn.Content;
                messages.Add(new { role, content = text });
            }
        }

        messages.Add(new { role = "user", content = prompt });

        var ollamaTools = _tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema
            }
        }).ToList();

        var modelName = _configuration["Ollama:Model"] ?? "mistral";
        _logger.LogDebug("AI chat using model {Model} with {HistoryCount} history messages.", modelName, messages.Count - 2);

        bool isToolCallLoop = true;
        int maxLoops = 5;
        int loopCount = 0;

        while (isToolCallLoop && loopCount < maxLoops)
        {
            loopCount++;
            isToolCallLoop = false; // assume no tool call unless we detect tool_calls array

            var requestPayload = new
            {
                model = modelName,
                messages = messages,
                stream = true,
                tools = ollamaTools.Any() ? ollamaTools : null
            };

            var jsonPayload = JsonSerializer.Serialize(requestPayload, PayloadJson);
            // Full payloads contain user prompts and portfolio data: Trace level only (off by default).
            _logger.LogTrace("Ollama request (loop {Loop}): {Payload}", loopCount, jsonPayload);

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = content };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Ollama chat returned {Status}: {Error}", (int)response.StatusCode, errorContent);
                yield return AiStreamEvent.Chunk($"Error: Failed to connect to Ollama model. Status Code: {response.StatusCode}.");
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            List<JsonElement> currentToolCalls = new();
            List<string> responseChunks = new();
            var bufferedContentBuilder = new StringBuilder();

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    // Check if Ollama sent tool_calls array
                    if (messageElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in toolCallsElement.EnumerateArray())
                        {
                            currentToolCalls.Add(tc.Clone());
                        }
                    }

                    if (messageElement.TryGetProperty("content", out var contentElement))
                    {
                        var text = contentElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            responseChunks.Add(text);
                            bufferedContentBuilder.Append(text);
                        }
                    }
                }
            }

            string bufferedContent = bufferedContentBuilder.ToString();

            if (currentToolCalls.Any())
            {
                isToolCallLoop = true;
                _logger.LogDebug("Model requested {Count} tool call(s).", currentToolCalls.Count);

                // Record the assistant's message with tool calls
                messages.Add(new
                {
                    role = "assistant",
                    content = bufferedContent,
                    tool_calls = currentToolCalls
                });

                // Execute all requested tools
                foreach (var tc in currentToolCalls)
                {
                    string toolName = "";
                    JsonElement argsElement = default;

                    if (tc.TryGetProperty("function", out var func))
                    {
                        if (func.TryGetProperty("name", out var nameElement))
                        {
                            toolName = nameElement.GetString() ?? "";
                        }
                        if (func.TryGetProperty("arguments", out var argsEl))
                        {
                            argsElement = argsEl;
                        }
                    }

                    var tool = _tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
                    var label = DescribeTool(toolName, argsElement);
                    yield return AiStreamEvent.Tool(toolName, "running", label);

                    string toolResult;
                    bool ok;
                    if (tool != null)
                    {
                        try
                        {
                            _logger.LogInformation("AI chat executing tool {Tool} for user {UserId}.", toolName, userId);
                            toolResult = await tool.ExecuteAsync(argsElement, userId);
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "AI chat tool {Tool} failed.", toolName);
                            toolResult = $"Error executing tool {toolName}: {ex.Message}";
                            ok = false;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("AI chat model requested unknown tool {Tool}.", toolName);
                        toolResult = $"Tool {toolName} not found.";
                        ok = false;
                    }

                    yield return AiStreamEvent.Tool(toolName, ok ? "completed" : "failed", label);

                    // Add tool result to message history
                    messages.Add(new { role = "tool", content = toolResult });
                }

                // Continue while loop to send tool execution results back to Ollama
            }
            else
            {
                // No tool calls requested: this is the final text response.
                foreach (var chunk in responseChunks)
                {
                    yield return AiStreamEvent.Chunk(chunk);
                }

                messages.Add(new { role = "assistant", content = bufferedContent });
                break;
            }
        }
    }

    /// <summary>Short, user-facing description of a tool call for the activity panel.</summary>
    private static string DescribeTool(string toolName, JsonElement args)
    {
        string? Arg(string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        return toolName switch
        {
            "get_stock_price" => $"Looking up {Arg("symbol") ?? "stock"} price",
            "get_binance_ticker" => $"Fetching {Arg("symbol") ?? "crypto"} ticker",
            "get_market_status" => "Checking CSE market status and indices",
            "get_top_gainers" => "Fetching today's top gainers",
            "get_top_losers" => "Fetching today's top losers",
            "get_market_news" => "Searching market news",
            "get_portfolio_holdings" => "Reading your portfolio holdings",
            "get_net_worth" => "Calculating your net worth",
            "get_profile" => "Reading your profile",
            _ => $"Running {toolName}",
        };
    }
}
