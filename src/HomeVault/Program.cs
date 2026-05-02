/*
 * FILE: Program.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Application entry point. Configures EF Core (SQL Server),
 *              cookie-based forms authentication, brute-force rate limiting
 *              on the login endpoint, security response headers, and the
 *              MVC pipeline.
 */

using HomeVault.Data;
using HomeVault.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ----- Services -----
builder.Services.AddControllersWithViews();

// EF Core: SQL Server. Connection string lives in user-secrets (locally) or
// in App Service Configuration (Azure) — never in source control.
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cookie auth: HTTPS-only, HttpOnly, SameSite=Lax, 30-min sliding expiry.
// Redirects to /Account/Login when an [Authorize] action is hit while signed-out.
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

// Anti-forgery cookie also locked to HTTPS in production.
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Rate limiting: brute-force protection for /Account/Login.
// Partitioned by remote IP — 5 attempts per minute, anything beyond returns
// 429 Too Many Requests with a Retry-After hint.
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

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
