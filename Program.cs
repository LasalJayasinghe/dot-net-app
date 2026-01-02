using dotnetApp;
using Serilog;
using Microsoft.EntityFrameworkCore;
using dotnetApp.Data;

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
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
