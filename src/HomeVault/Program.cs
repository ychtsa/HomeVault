/*
 * FILE: Program.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Application entry point. Configures EF Core (SQL Server),
 *              cookie-based forms authentication, the MVC pipeline, and
 *              seeds demo data on first startup.
 */

using HomeVault.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----- Services -----
builder.Services.AddControllersWithViews();

// EF Core: SQL Server backed by the connection string in user-secrets / env.
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cookie auth: the only auth scheme; redirects to /Account/Login when needed.
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
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// ----- One-shot DB seed (dev convenience) -----
// Applies any pending EF migrations and inserts demo data the first time.
using (var scope = app.Services.CreateScope())
{
    CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    context.Database.Migrate();
    DbInitializer.Seed(context);
}

// ----- HTTP pipeline -----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();