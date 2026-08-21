using System.Text.Json;

namespace dotnetApp.Application.Services.Ai;

public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    
    // JSON schema for the parameters object (e.g., { type: "object", properties: { ... }, required: [...] })
    object ParametersSchema { get; }
    
    Task<string> ExecuteAsync(JsonElement parameters, string userId);
}
