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

// Memory Cache
builder.Services.AddMemoryCache();

// Add services to the container.
builder.Services.AddControllersWithViews();
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
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<TokenService>();

builder.Services.AddHttpClient<TelegramService>();

builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<IStrategy, EmaRsiStrategy>();
builder.Services.AddSingleton<TradingBotService>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceService>());
builder.Services.AddHostedService<AlertJob>();

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));

// builder.Services
//     .AddIdentity<ApplicationUser, IdentityRole>()
//     .AddEntityFrameworkStores<AppDbContext>()
//     .AddDefaultTokenProviders();

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
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
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
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    // options.AddPolicy("AllowFrontend", policy =>
    // {
    //     policy.WithOrigins("http://localhost:3000")
    //           .AllowAnyHeader()
    //           .AllowAnyMethod();
    // });
});

var app = builder.Build();

var binance = app.Services.GetRequiredService<BinanceService>();
var bot = app.Services.GetRequiredService<TradingBotService>();

binance.CandleClosed += async (c) => await bot.OnCandleClosed(c);


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
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
