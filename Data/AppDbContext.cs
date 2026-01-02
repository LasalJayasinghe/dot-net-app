using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}
    public DbSet<Alert> Alerts { get; set; } = null!;
}
