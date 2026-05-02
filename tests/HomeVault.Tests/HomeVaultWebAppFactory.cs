/*
 * FILE: HomeVaultWebAppFactory.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Test host that boots the real HomeVault application but
 *              swaps the SQL Server DbContext registration for an
 *              in-memory provider. Each factory instance gets a fresh
 *              database name so tests don't share state.
 *              Production cookie policies (SecurePolicy=Always) are
 *              relaxed to SameAsRequest because the in-process test
 *              client uses plain HTTP.
 */

using HomeVault.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVault.Tests
{
    public class HomeVaultWebAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = "homevault-tests-" + Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Strip out the real DbContextOptions registered by Program.cs.
                ServiceDescriptor? options = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>));
                if (options != null) services.Remove(options);

                ServiceDescriptor? context = services.SingleOrDefault(
                    d => d.ServiceType == typeof(CatalogDbContext));
                if (context != null) services.Remove(context);

                // Replace with an isolated in-memory database.
                services.AddDbContext<CatalogDbContext>(opt =>
                    opt.UseInMemoryDatabase(_databaseName));

                // The TestServer's HttpClient sends plain HTTP, so any cookie
                // marked Secure would silently fail to round-trip. Relaxing
                // both the antiforgery cookie and the auth cookie to
                // SameAsRequest lets the integration tests exercise the same
                // code paths as production without dropping cookies.
                services.PostConfigure<AntiforgeryOptions>(opts =>
                    opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

                services.PostConfigure<CookieAuthenticationOptions>(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    opts => opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
            });
        }
    }
}
