namespace dotnetApp.Application.ViewModels.Ai;

public class AiChatRequestViewModel
{
    public string Prompt { get; set; } = string.Empty;
    public string? ConversationId { get; set; }

    /// <summary>Recent earlier turns (oldest first) so the assistant remembers the conversation.</summary>
    public List<AiChatTurnViewModel>? History { get; set; }
}

public class AiChatTurnViewModel
{
    public string Role { get; set; } = string.Empty; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}
