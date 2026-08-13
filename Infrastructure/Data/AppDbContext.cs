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
    public DbSet<Portfolio> Portfolios { get; set; } = null!;
    public DbSet<PortfolioHolding> PortfolioHoldings { get; set; } = null!;
    public DbSet<CurrencyExchangeRate> CurrencyExchangeRates { get; set; } = null!;
    public DbSet<OtpRecord> OtpRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<OtpRecord>(entity =>
        {
            entity.ToTable("OtpRecords");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Email).IsRequired().HasMaxLength(255);
            entity.Property(o => o.Code).IsRequired().HasMaxLength(10);
            entity.Property(o => o.Purpose).IsRequired().HasMaxLength(50);
            entity.HasIndex(o => new { o.Email, o.Purpose, o.IsUsed });
        });

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

        // ── Portfolio ──────────────────────────────────────────────────────────
        builder.Entity<Portfolio>(entity =>
        {
            entity.ToTable("Portfolios");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name).IsRequired().HasMaxLength(100);
            entity.Property(p => p.BaseCurrency).IsRequired().HasMaxLength(10);
            entity.Property(p => p.Type).HasConversion<string>();
            entity.Property(p => p.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            entity.HasOne(p => p.User)
                .WithMany(u => u.Portfolios)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PortfolioHolding ───────────────────────────────────────────────────
        builder.Entity<PortfolioHolding>(entity =>
        {
            entity.ToTable("PortfolioHoldings");
            entity.HasKey(h => h.Id);

            entity.Property(h => h.Symbol).IsRequired().HasMaxLength(30);
            entity.Property(h => h.AssetType).HasConversion<string>();
            entity.Property(h => h.Quantity).HasColumnType("decimal(28,8)");
            entity.Property(h => h.AverageBuyPrice).HasColumnType("decimal(28,8)");
            entity.Property(h => h.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(h => h.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            // A portfolio cannot have duplicate symbols
            entity.HasIndex(h => new { h.PortfolioId, h.Symbol }).IsUnique();

            entity.HasOne(h => h.Portfolio)
                .WithMany(p => p.Holdings)
                .HasForeignKey(h => h.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CurrencyExchangeRate ───────────────────────────────────────────────
        builder.Entity<CurrencyExchangeRate>(entity =>
        {
            entity.ToTable("CurrencyExchangeRates");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Rate).HasColumnType("decimal(18,6)");
            entity.Property(r => r.LastUpdated).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            // e.g. only one USDT -> LKR row at any time
            entity.HasIndex(r => new { r.FromCurrency, r.ToCurrency }).IsUnique();
        });
    }

}
