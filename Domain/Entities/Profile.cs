using Microsoft.AspNetCore.Identity;

public class Profile
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? TelegramId { get; set; }
    public string UserId { get; set; } = null!;

    // Navigation property
    public ApplicationUser User { get; set; } = null!;
}