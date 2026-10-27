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
using HomeVault.Services;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using QuestPDF.Infrastructure;
using Serilog;
using System.Threading.RateLimiting;

// QuestPDF is free for personal use, OSS, and companies under $1M revenue
// under the Community license. Required to be set before any PDF is rendered.
QuestPDF.Settings.License = LicenseType.Community;

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

    var migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
    var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate-only").ToArray());

    // ----- Logging -----
    builder.Host.UseSerilog();

    // ----- Services -----
    builder.Services.AddControllersWithViews();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ApiExceptionHandler>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "HomeVault API", Version = "v1",
            Description = "Private household inventory. Sign in through /api/auth/login. " +
                "Cookie authentication and X-CSRF-TOKEN protect writes. Swagger UI obtains CSRF tokens automatically."
        });
        options.DocInclusionPredicate((_, description) =>
            description.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true);
    });

    // Persist the cookie key ring across container replacements and App Service instances.
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("HomeVault");
    if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Enable only behind an ingress which prevents direct access to Kestrel.
        if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
        {
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
        foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    });

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
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                else context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

    // Anti-forgery cookie locked to HTTPS as well.
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.HeaderName = "X-CSRF-TOKEN";
    });

    // Rate limiting:
    //   "login"          - 5 / IP / minute. Caps brute-force on credentials.
    //   "forgotPassword" - 3 / IP / 10 min. Caps email-bombing and DB-write
    //                      abuse from anonymous attackers.
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

        options.AddPolicy("forgotPassword", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0
                }));

        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsync(
                "Too many requests. Please try again later.", token);
        };
    });

    // Health checks: a /health endpoint that pings the database. Useful for
    // load balancers and uptime monitors to verify the app is fully alive.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<CatalogDbContext>("database");

    // Email transport. LogEmailSender writes outgoing email to the app log
    // — fine for development. Swap for an SMTP / SendGrid implementation
    // in production.
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();

    // Per-catalog image storage. Backed by the local filesystem under
    // App_Data/uploads/. Production deployments could substitute Azure Blob
    // Storage, S3, etc., behind the same interface.
    var storageProvider = builder.Configuration["Storage:Provider"] ?? "FileSystem";
    if (storageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton(_ =>
        {
            var connectionString = builder.Configuration["Storage:ConnectionString"];
            var service = !string.IsNullOrWhiteSpace(connectionString)
                ? new BlobServiceClient(connectionString)
                : new BlobServiceClient(
                    new Uri(builder.Configuration["Storage:BlobServiceUri"]
                        ?? throw new InvalidOperationException("Storage:BlobServiceUri is required for AzureBlob storage.")),
                    new DefaultAzureCredential());
            return service.GetBlobContainerClient(builder.Configuration["Storage:ContainerName"] ?? "catalog-images");
        });
        builder.Services.AddSingleton<ICatalogImageStorage, AzureBlobCatalogImageStorage>();
    }
    else if (storageProvider.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<ICatalogImageStorage, FilesystemCatalogImageStorage>();
    else
        throw new InvalidOperationException($"Unknown storage provider: {storageProvider}");

    // PDF insurance-report generator (QuestPDF).
    builder.Services.AddSingleton<IInsuranceReportGenerator, QuestPdfInsuranceReportGenerator>();

    var app = builder.Build();

    // ----- Apply migrations on startup -----
    // Real (relational) providers run migrations; in-memory providers used
    // by tests do not support Migrate() and use EnsureCreated() instead.
    if (migrateOnly || builder.Configuration.GetValue("Database:ApplyMigrations", app.Environment.IsDevelopment()))
    {
        using var scope = app.Services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (context.Database.IsRelational())
        {
            context.Database.Migrate();
        }
        else
        {
            context.Database.EnsureCreated();
        }
    }
    if (migrateOnly) return;

    // ----- HTTP pipeline -----
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
        app.UseForwardedHeaders();

    app.UseExceptionHandler("/Home/Error");
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    // App Service probes its container over HTTP; all application traffic still requires HTTPS.
    app.UseWhen(context => !context.Request.Path.Equals("/health"), branch => branch.UseHttpsRedirection());

    // Security headers applied to every response (including static files).
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.Headers.CacheControl = "no-store";
        await next();
    });

    app.UseStatusCodePages(async context =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
            await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
                .ExecuteAsync(context.HttpContext);
    });

    app.UseStaticFiles();

    // Log every request with method, path, status, and duration.
    app.UseSerilogRequestLogging();

    app.UseRouting();

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    if (builder.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.IndexStream = () => typeof(Program).Assembly
                .GetManifestResourceStream("HomeVault.OpenApi.index.html")!;
        });
    }

    app.MapHealthChecks("/health");
    app.MapControllers();
    app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "HomeVault terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so test projects (WebApplicationFactory<Program>) can host this app.
public partial class Program { }
