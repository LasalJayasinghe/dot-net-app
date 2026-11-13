using System.Text.Json;
using dotnetApp.Application.Services;
using dotnetApp.Application.ViewModels.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/aichat")]
[Authorize]
public class AiChatApiController : ControllerBase
{
    private readonly AiAgentService _aiAgentService;
    private readonly Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> _userManager;

    public AiChatApiController(AiAgentService aiAgentService, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager)
    {
        _aiAgentService = aiAgentService;
        _userManager = userManager;
    }

    /// <summary>
    /// Streams the assistant's reply as Server-Sent Events:
    /// <list type="bullet">
    /// <item><c>data: &lt;text&gt;</c> — a reply chunk; newlines are escaped as a literal backslash-n so each chunk stays on one line.</item>
    /// <item><c>tool: {"name","status","description"}</c> — tool activity (running/completed/failed). Clients that don't know this field ignore it.</item>
    /// </list>
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] AiChatRequestViewModel request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var history = request.History?
            .Select(t => new ChatTurn(t.Role, t.Content))
            .ToList();

        await foreach (var ev in _aiAgentService.StreamChatAsync(request.Prompt, history, user.Id, HttpContext.RequestAborted))
        {
            if (ev.Type == "tool")
            {
                var json = JsonSerializer.Serialize(new { name = ev.ToolName, status = ev.Status, description = ev.Description });
                await Response.WriteAsync("tool: " + json + "\n\n");
            }
            else
            {
                var escapedChunk = (ev.Text ?? string.Empty).Replace("\n", "\\n"); // keep each chunk on one SSE line
                await Response.WriteAsync("data: " + escapedChunk + "\n\n");
            }
            await Response.Body.FlushAsync();
        }
        // The reply ends when the stream closes. Current web/mobile clients also accept "data: [DONE]",
        // but it isn't sent so that older deployed web builds (which would print it) keep working.
    }
}
