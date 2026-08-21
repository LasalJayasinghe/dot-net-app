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
        var messages = new List<object>
        {
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

        bool isToolCallLoop = true;
        int maxLoops = 5;
        int loopCount = 0;

        while (isToolCallLoop && loopCount < maxLoops)
        {
            loopCount++;
            isToolCallLoop = false; // assume it's just text unless we see a tool call
            
            var requestPayload = new
            {
                model = modelName,
                messages = messages,
                stream = true,
                tools = ollamaTools.Any() ? ollamaTools : null
            };

            var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = content };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                yield return $"Error: Failed to connect to Ollama model. Status Code: {response.StatusCode}";
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            List<JsonElement> currentToolCalls = new();
            bool isStreamingTextToUser = false;
            string bufferedContent = "";

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    // If it's a tool call, Ollama includes tool_calls array
                    if (messageElement.TryGetProperty("tool_calls", out var toolCallsElement))
                    {
                        isToolCallLoop = true;
                        foreach (var tc in toolCallsElement.EnumerateArray())
                        {
                            currentToolCalls.Add(tc.Clone());
                        }
                    }
                    else if (messageElement.TryGetProperty("content", out var contentElement))
                    {
                        var text = contentElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (!isToolCallLoop)
                            {
                                isStreamingTextToUser = true;
                                yield return text;
                            }
                            else
                            {
                                // Sometimes model emits content while making tool call, buffer it just in case
                                bufferedContent += text;
                            }
                        }
                    }
                }
            }

            if (isToolCallLoop && currentToolCalls.Any())
            {
                // Record the assistant's tool call message
                messages.Add(new { role = "assistant", content = bufferedContent, tool_calls = currentToolCalls });

                // Execute all tools
                foreach (var tc in currentToolCalls)
                {
                    if (tc.TryGetProperty("function", out var func) &&
                        func.TryGetProperty("name", out var nameElement) &&
                        func.TryGetProperty("arguments", out var argsElement))
                    {
                        var toolName = nameElement.GetString();
                        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                        
                        string toolResult = "";
                        if (tool != null)
                        {
                            try 
                            {
                                toolResult = await tool.ExecuteAsync(argsElement, userId);
                            }
                            catch (Exception ex)
                            {
                                toolResult = $"Error executing tool: {ex.Message}";
                            }
                        }
                        else
                        {
                            toolResult = $"Tool {toolName} not found.";
                        }

                        // Add tool result to history
                        messages.Add(new { role = "tool", content = toolResult });
                    }
                }
                
                // The while loop will now continue and send the tool results back to Ollama
            }
        }
    }
}
