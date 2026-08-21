namespace dotnetApp.Application.ViewModels.Ai;

public class AiChatRequestViewModel
{
    public string Prompt { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
}
