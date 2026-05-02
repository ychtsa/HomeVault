/*
 * FILE: Program.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Application entry point. Configures Serilog (console + rolling
 *              file sink), EF Core (SQL Server with retry on transient
 *              failures), cookie-based forms authentication, brute-force rate
 *              limiting on the login endpoint, security response headers,
 *              health checks, and the MVC pipeline.
 */

using HomeVault.Data;
using HomeVault.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Threading.RateLimiting;

// ----- Bootstrap logger -----
// A static logger is created up-front so any startup failure is captured.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/homevault-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

try
{
    Log.Information("Starting HomeVault");

    var builder = WebApplication.CreateBuilder(args);

    // ----- Logging -----
    builder.Host.UseSerilog();

    // ----- Services -----
    builder.Services.AddControllersWithViews();

    // EF Core: SQL Server. Connection string lives in user-secrets (locally) or
    // in App Service Configuration (production) — never in source control.
    // EnableRetryOnFailure() makes the data layer resilient to transient SQL
    // outages (network blips, DB failover, throttling) by retrying failed
    // queries automatically with exponential backoff.
    builder.Services.AddDbContext<CatalogDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null)));

    // Cookie auth: HTTPS-only, HttpOnly, SameSite=Lax, 30-min sliding expiry.
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/Login";
            options.Cookie.Name = "HomeVault.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
        });

    // Anti-forgery cookie locked to HTTPS as well.
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

    // Rate limiting: brute-force protection for /Account/Login.
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsync(
                "Too many login attempts. Please try again in a minute.", token);
        };
    });

    // Health checks: a /health endpoint that pings the database. Useful for
    // load balancers and uptime monitors to verify the app is fully alive.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<CatalogDbContext>("database");

    var app = builder.Build();

    // ----- Apply migrations on startup -----
    using (var scope = app.Services.CreateScope())
    {
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        context.Database.Migrate();
    }

    // ----- HTTP pipeline -----
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // Security headers applied to every response (including static files).
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseStaticFiles();

    // Log every request with method, path, status, and duration.
    app.UseSerilogRequestLogging();

    app.UseRouting();

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health");

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "HomeVault terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so test projects (WebApplicationFactory<Program>) can host this app.
public partial class Program { }
