using dotnetApp;
using Serilog;
using Microsoft.EntityFrameworkCore;
using dotnetApp.Data;
using Microsoft.AspNetCore.Identity;
using dotnetApp.Data.Seeders;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");

    options.UseMySql(
        cs,
        ServerVersion.AutoDetect(cs)
    );
});

// Use Serilog instead of default logging
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<StockService>();
builder.Services.AddHttpClient<TelegramService>();

builder.Services.AddSingleton<BinanceService>();

builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<AppDbContext>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceService>());
builder.Services.AddHostedService<AlertJob>();

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardAccess", policy =>
        policy.RequireClaim("permission", "dashboard.access"));

    options.AddPolicy("AlertCreate", policy =>
        policy.RequireClaim("permission", "alert.create"));

    options.AddPolicy("AlertEdit", policy =>
        policy.RequireClaim("permission", "alert.edit"));

    options.AddPolicy("AlertView", policy =>
        policy.RequireClaim("permission", "alert.view"));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/Home/AccessDenied";
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        if (db.Database.CanConnect())
        {
            Console.WriteLine("✅ Database connection SUCCESS");
        }
        else
        {
            Console.WriteLine("❌ Database connection FAILED");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ Database ERROR: " + ex.Message);
    }

    await RoleSeeder.SeedRolesAndPermissionsAsync(scope.ServiceProvider);
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
