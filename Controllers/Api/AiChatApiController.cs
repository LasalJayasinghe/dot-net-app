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

        await foreach (var chunk in _aiAgentService.StreamChatAsync(request.Prompt, user.Id, HttpContext.RequestAborted))
        {
            // Format as Server-Sent Events
            var escapedChunk = chunk.Replace("\n", "\\n"); // prevent multiline break in SSE
            await Response.WriteAsync($"data: {escapedChunk}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}
