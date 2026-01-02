using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace dotnetApp.Data;

public class AppDbContext : IdentityDbContext<User>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Alert> Alerts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<IdentityUser>(entity =>
        {
            entity.Property(u => u.NormalizedUserName).HasMaxLength(191);
            entity.Property(u => u.NormalizedEmail).HasMaxLength(191);
        });

        builder.Entity<IdentityRole>(entity =>
        {
            entity.Property(r => r.NormalizedName).HasMaxLength(191);
        });
    }
}
