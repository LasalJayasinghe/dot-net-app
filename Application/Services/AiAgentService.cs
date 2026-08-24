using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using dotnetApp.Application.Services.Ai;
using dotnetApp.Application.Services.Ai.Tools;

namespace dotnetApp.Application.Services;

public class AiAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IEnumerable<IMcpTool> _tools;
    private readonly IConfiguration _configuration;

    public AiAgentService(HttpClient httpClient, IEnumerable<IMcpTool> tools, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _tools = tools;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(string prompt, string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
8. When tool results are returned, synthesize the data into a helpful, clear, and professional response for the user.";

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = prompt }
        };

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
        Console.WriteLine($"[AiAgentService] Model name: {modelName}");
        
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

            var options = new JsonSerializerOptions 
            { 
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull 
            };
            var jsonPayload = JsonSerializer.Serialize(requestPayload, options);
            
            Console.WriteLine($"\n=== OLLAMA REQUEST (Loop {loopCount}) ===");
            var debugOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull 
            };
            Console.WriteLine($"Payload:\n{JsonSerializer.Serialize(requestPayload, debugOptions)}");
            
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = content };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            Console.WriteLine($"\n=== OLLAMA RESPONSE (Loop {loopCount}) ===");
            Console.WriteLine($"Status Code: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Error Details: {errorContent}");
                Console.WriteLine($"=======================\n");
                yield return $"Error: Failed to connect to Ollama model. Status Code: {response.StatusCode}. Details: {errorContent}";
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
                Console.WriteLine($"[AiAgentService] Model requested {currentToolCalls.Count} tool call(s). Buffering content and executing tools...");

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

                    string toolResult;
                    var tool = _tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
                    
                    if (tool != null)
                    {
                        try 
                        {
                            Console.WriteLine($"[AiAgentService] Executing tool '{toolName}' for user '{userId}' with args: {argsElement}");
                            toolResult = await tool.ExecuteAsync(argsElement, userId);
                            Console.WriteLine($"[AiAgentService] Tool '{toolName}' result: {toolResult}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AiAgentService] Tool '{toolName}' execution exception: {ex.Message}");
                            toolResult = $"Error executing tool {toolName}: {ex.Message}";
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[AiAgentService] Tool '{toolName}' not found.");
                        toolResult = $"Tool {toolName} not found.";
                    }

                    // Add tool result to message history
                    messages.Add(new { role = "tool", content = toolResult });
                }
                
                // Continue while loop to send tool execution results back to Ollama
            }
            else
            {
                // No tool calls requested: this is the final text response.
                // Yield buffered response chunks to the UI stream.
                foreach (var chunk in responseChunks)
                {
                    yield return chunk;
                }

                messages.Add(new { role = "assistant", content = bufferedContent });
                break;
            }
        }
    }
}
