using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace dotnetApp.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Alert> Alerts { get; set; } = null!;
    public DbSet<Stocks> Stocks { get; set; } = null!;
    public DbSet<MarketStatus> MarketStatus { get; set; } = null!;
    public DbSet<MarketIndices> MarketIndices { get; set; } = null!;
    public DbSet<Profile> Profiles { get; set; } = null!;
    public DbSet<WatchlistItem> WatchlistItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // builder.Entity<IdentityUser>(entity =>
        // {
        //     entity.Property(u => u.NormalizedUserName).HasMaxLength(191);
        //     entity.Property(u => u.NormalizedEmail).HasMaxLength(191);
        // });

        builder.Entity<IdentityRole>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(191);
            entity.Property(r => r.NormalizedName).HasMaxLength(191);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.NormalizedUserName).HasMaxLength(191);
            entity.Property(u => u.NormalizedEmail).HasMaxLength(191);
        });

        builder.Entity<Stocks>()
            .HasIndex(s => s.Symbol)
            .IsUnique();

        builder.Entity<MarketStatus>(entity =>
        {
            entity.ToTable("cse_MarketStatus");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedNever();
        });

        builder.Entity<MarketIndices>(entity =>
        {
            entity.ToTable("cse_MarketIndices");
        });

        builder.Entity<MarketIndices>()
            .Property(e => e.IndexType)
            .HasConversion<string>();

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Profile)
            .WithOne(p => p.User)
            .HasForeignKey<Profile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Profile>(entity =>
        {
            entity.ToTable("profile");
            entity.HasIndex(p => p.UserId);
        });

        builder.Entity<WatchlistItem>(entity =>
        {
            entity.ToTable("WatchlistItems");

            entity.HasKey(w => w.Id);

            entity.Property(w => w.UserId)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(w => w.Symbol)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(w => w.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            entity.HasIndex(w => new { w.UserId, w.Symbol })
                .IsUnique();

            entity.HasOne(w => w.User)
                .WithMany(u => u.WatchlistItems)
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

}
