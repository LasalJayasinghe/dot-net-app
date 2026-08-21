using dotnetApp;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using dotnetApp.Infrastructure.Data;
using dotnetApp.Infrastructure.Data.Seeders;
using dotnetApp.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using dotnetApp.Application.Interface;
using dotnetApp.Application.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

// Required for ExcelDataReader
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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

// Memory Cache
builder.Services.AddMemoryCache();

// Add services to the container.
builder.Services.AddControllers();

// SignalR — required for real-time Crypto Dashboard push
builder.Services.AddSignalR();
builder.Services.AddHttpClient<StockService>(client =>
{
    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Accept", "application/json, text/plain, */*");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Origin", "https://www.cse.lk");
});

builder.Services.AddScoped<StockRepository>();
builder.Services.AddScoped<AlertRepository>();
builder.Services.AddScoped<ProfileRepository>();
// builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IPortfolioFileSyncService, PortfolioFileSyncService>();

builder.Services.AddHttpClient<TelegramService>();
builder.Services.AddHttpClient<IBrevoEmailService, BrevoEmailService>();

builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<IStrategy, EmaRsiStrategy>();
builder.Services.AddSingleton<TradingBotService>();

// Crypto Dashboard services
builder.Services.AddScoped<ICryptoMarketService, CryptoMarketService>();
builder.Services.AddScoped<IAiMarketSummaryService, AiMarketSummaryService>();
builder.Services.AddScoped<dotnetApp.Application.Services.PortfolioService>();

// AI Chat Integration
builder.Services.AddHttpClient<AiAgentService>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetNetWorthTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetStockDataTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetBinanceTickerTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetMarketStatusTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetTopGainersTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetTopLosersTool>();
builder.Services.AddScoped<dotnetApp.Application.Services.Ai.IMcpTool, dotnetApp.Application.Services.Ai.Tools.GetProfileTool>();

// Named HttpClient for Binance REST API — no API keys needed for public endpoints
builder.Services.AddHttpClient("binance", client =>
{
    client.BaseAddress = new Uri("https://api.binance.com");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
});

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

// Cookie related authentication
// builder.Services.ConfigureApplicationCookie(options =>
// {
//     options.LoginPath = "/Auth/Login";
//     options.AccessDeniedPath = "/Home/AccessDenied";
// });

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "SuperSecretDefaultKeyForDevPurposes12345!")
        ),

        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true) // allows any origin
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for SignalR cross-origin
    });
});

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (cs != null)
{
    builder.Services.AddHealthChecks()
        .AddMySql(cs);
}

var app = builder.Build();

var binance = app.Services.GetRequiredService<BinanceService>();
var bot = app.Services.GetRequiredService<TradingBotService>();

binance.CandleClosed += async (c) => await bot.OnCandleClosed(c);


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
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
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Map("/error", () => Results.Problem());

// SignalR hub endpoint for the Crypto Trading Dashboard
app.MapHub<CryptoHub>("/hubs/crypto");

app.MapHealthChecks("/health");


app.Run();
