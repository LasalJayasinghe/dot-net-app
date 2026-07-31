public class WatchlistItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Symbol { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}
