using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HomeVault.Data;
using HomeVault.Models.Api;
using HomeVault.Models.Entities;
using HomeVault.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HomeVault.Tests;

public sealed class ApiIntegrationTests
{
    private const string Password = "Password123!";
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1EAAAAASUVORK5CYII=");

    [Fact]
    public async Task ProtectedApiEndpoints_Return401WithoutLoginRedirect()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);

        foreach (string path in new[] { "/api/auth/me", "/api/items", "/api/items/report", "/api/items/abcde/image" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.True(csrf.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task SignupLoginLogout_UsesCookieAndRequiresCsrfForCurrentIdentity()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);

        using var missingCsrf = await client.PostAsJsonAsync("/api/auth/signup", Signup("alice"));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        await RefreshCsrf(client);
        string anonymousToken = client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();

        using var signup = await client.PostAsJsonAsync("/api/auth/signup", Signup("alice"));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        UserResponse user = (await login.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.Equal("alice", user.Username);
        Assert.Equal("Resident alice", user.ResidentName);
        Assert.Equal(5, user.CatalogId.Length);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("HomeVault.Auth=") && cookie.Contains("httponly"));
        Assert.DoesNotContain("password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(user, await client.GetFromJsonAsync<UserResponse>("/api/auth/me"));
        using var staleToken = await client.PostAsJsonAsync("/api/items", Item("Blocked"));
        Assert.Equal(HttpStatusCode.BadRequest, staleToken.StatusCode);

        await RefreshCsrf(client);
        Assert.NotEqual(anonymousToken, client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ResidentUsers.SingleAsync();
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, stored.PasswordHash));
    }

    [Fact]
    public async Task Signup_ValidatesFieldsAndRejectsDuplicateAccounts()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        await RefreshCsrf(client);

        using var invalid = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            residentName = "", residentAddress = "Address", username = "x", email = "invalid",
            password = "weak", confirmPassword = "different"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("errors", await invalid.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ResidentUsers.ToListAsync());

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/signup", Signup("alice"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/signup", Signup("alice"))).StatusCode);
        using var scopeAfterSignup = factory.Services.CreateScope();
        Assert.Single(await scopeAfterSignup.ServiceProvider.GetRequiredService<CatalogDbContext>().ResidentUsers.ToListAsync());
    }

    [Fact]
    public async Task Login_TracksFailuresAndHonorsSharedAccountLockout()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        await RefreshCsrf(client);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/auth/signup", Signup("locked"))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await db.ResidentUsers.SingleAsync()).FailedLoginAttempts = 9;
            await db.SaveChangesAsync();
        }

        using var badPassword = await client.PostAsJsonAsync("/api/auth/login", new { username = "locked", password = "Wrong123" });
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);
        using var locked = await client.PostAsJsonAsync("/api/auth/login", new { username = "locked", password = Password });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var user = await db.ResidentUsers.SingleAsync();
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.True(user.LockedUntil > DateTime.UtcNow);
            user.LockedUntil = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        using var unlocked = await client.PostAsJsonAsync("/api/auth/login", new { username = "locked", password = Password });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task ItemCrud_IsTenantScopedIncludingImagesAndReports()
    {
        await using var factory = new ApiWebAppFactory();
        using var alice = Client(factory);
        using var bob = Client(factory);
        var aliceUser = await RegisterAndLogin(alice, "alice");
        var bobUser = await RegisterAndLogin(bob, "bob");

        // A client cannot assign ownership by adding a catalogId to the JSON.
        using var created = await alice.PostAsJsonAsync("/api/items", new
        {
            itemName = "Alice Camera", itemType = "Electronics", itemValue = 800, catalogId = bobUser.CatalogId
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        ItemResponse item = (await created.Content.ReadFromJsonAsync<ItemResponse>())!;
        Assert.EndsWith($"/api/items/{item.ItemId}", created.Headers.Location!.ToString());
        Assert.Single((await alice.GetFromJsonAsync<ItemResponse[]>("/api/items"))!);
        Assert.Empty((await bob.GetFromJsonAsync<ItemResponse[]>("/api/items"))!);

        using var upload = await alice.PostAsync($"/api/items/{item.ItemId}/image", ImageBody(Png));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal($"/api/items/{item.ItemId}/image", (await upload.Content.ReadFromJsonAsync<ItemResponse>())!.ImageUrl);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/items/{item.ItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/items/{item.ItemId}", Item("Stolen"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/items/{item.ItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/items/{item.ItemId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/items/{item.ItemId}/image", ImageBody(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/items/{item.ItemId}/image")).StatusCode);

        using var updated = await alice.PutAsJsonAsync($"/api/items/{item.ItemId}", Item("Updated camera", 1000));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1000, (await alice.GetFromJsonAsync<ItemResponse>($"/api/items/{item.ItemId}"))!.ItemValue);

        using var bobReport = await bob.GetAsync("/api/items/report");
        Assert.Equal("application/pdf", bobReport.Content.Headers.ContentType?.MediaType);
        Assert.Empty(factory.Report.Items);
        Assert.Equal(bobUser.CatalogId, factory.Report.Resident!.CatalogId);

        using var aliceReport = await alice.GetAsync("/api/items/report");
        Assert.Equal(HttpStatusCode.OK, aliceReport.StatusCode);
        Assert.Equal("attachment", aliceReport.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(item.ItemId, Assert.Single(factory.Report.Items).ItemId);
        Assert.Equal(aliceUser.CatalogId, factory.Report.Resident!.CatalogId);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/items/{item.ItemId}")).StatusCode);
        Assert.Empty(factory.Storage.Images);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/items/{item.ItemId}")).StatusCode);
    }

    [Fact]
    public async Task ItemValidationAndCsrf_RejectInvalidOrForgedMutations()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        await RegisterAndLogin(client, "alice");

        using var invalid = await client.PostAsJsonAsync("/api/items", new
        {
            itemName = new string('x', 31), itemType = "", itemValue = -1
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("errors", await invalid.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<ItemResponse[]>("/api/items"))!);

        using var created = await client.PostAsJsonAsync("/api/items", Item("Original"));
        var item = (await created.Content.ReadFromJsonAsync<ItemResponse>())!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/items", Item("Forged"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/items/{item.ItemId}", Item("Forged"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/items/{item.ItemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/items/{item.ItemId}/image", ImageBody(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/items/{item.ItemId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal("Original", (await client.GetFromJsonAsync<ItemResponse>($"/api/items/{item.ItemId}"))!.ItemName);
        Assert.Empty(factory.Storage.Images);
    }

    [Fact]
    public async Task ImageUpload_ValidatesContentsAndRoundTripsThroughPrivateEndpoint()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        await RegisterAndLogin(client, "alice");
        using var created = await client.PostAsJsonAsync("/api/items", Item("Camera"));
        var item = (await created.Content.ReadFromJsonAsync<ItemResponse>())!;
        string url = $"/api/items/{item.ItemId}/image";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, ImageBody("not a PNG"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, ImageBody(Png, "text/html"))).StatusCode);
        using var oversized = await client.PostAsync(url, ImageBody(new byte[5 * 1024 * 1024 + 1]));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Empty(factory.Storage.Images);

        using var upload = await client.PostAsync(url, ImageBody(Png));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using var downloaded = await client.GetAsync(url);
        Assert.Equal("image/png", downloaded.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await downloaded.Content.ReadAsByteArrayAsync());
        Assert.True(downloaded.Headers.CacheControl?.NoStore);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<ItemResponse>($"/api/items/{item.ItemId}"))!.ImageUrl);
        Assert.Empty(factory.Storage.Images);
    }

    [Fact]
    public async Task ApiFailure_ReturnsPrivateProblemJsonWithoutExceptionDetails()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        await RegisterAndLogin(client, "alice");
        factory.Report.ThrowOnGenerate = true;

        using var response = await client.GetAsync("/api/items/report");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("private storage credential", await response.Content.ReadAsStringAsync());
        Assert.Contains("traceId", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_HidesSwaggerAndAllowsHttpHealthProbes()
    {
        await using var factory = new ApiWebAppFactory { Production = true };
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Null(health.Headers.Location);
        using var swagger = await client.GetAsync("https://localhost/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
    }

    [Fact]
    public async Task SwaggerUi_UsesLocalScriptsCompatibleWithContentSecurityPolicy()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("/js/swagger-init.js", html);
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        foreach (string path in new[] { "/swagger/swagger-ui.css", "/swagger/swagger-ui-bundle.js", "/js/swagger-init.js" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Swagger_DescribesApiAndMultipartUpload()
    {
        await using var factory = new ApiWebAppFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string specification = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/auth/login", specification);
        Assert.Contains("/api/items/{id}/image", specification);
        Assert.Contains("multipart/form-data", specification);
    }

    private static HttpClient Client(ApiWebAppFactory factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    private static object Signup(string username) => new
    {
        residentName = $"Resident {username}", residentAddress = "123 Main Street", username,
        email = $"{username}@example.com", password = Password, confirmPassword = Password
    };

    private static object Item(string name, int value = 100) => new
    {
        itemName = name, itemType = "Electronics", itemValue = value
    };

    private static async Task RefreshCsrf(HttpClient client)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);
    }

    private static async Task<UserResponse> RegisterAndLogin(HttpClient client, string username)
    {
        await RefreshCsrf(client);
        using var signup = await client.PostAsJsonAsync("/api/auth/signup", Signup(username));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await RefreshCsrf(client);
        return (await login.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    private static MultipartFormDataContent ImageBody(byte[] bytes, string contentType = "image/png")
    {
        var body = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Add(file, "file", "photo.png");
        return body;
    }

    private sealed class ApiWebAppFactory : HomeVaultWebAppFactory
    {
        public bool Production { get; init; }
        public MemoryImageStorage Storage { get; } = new();
        public CaptureReportGenerator Report { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (Production)
                builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICatalogImageStorage>();
                services.RemoveAll<IInsuranceReportGenerator>();
                services.AddSingleton<ICatalogImageStorage>(Storage);
                services.AddSingleton<IInsuranceReportGenerator>(Report);
            });
        }
    }

    private sealed class MemoryImageStorage : ICatalogImageStorage
    {
        public ConcurrentDictionary<(string Catalog, string Item), byte[]> Images { get; } = new();

        public async Task SaveAsync(string catalogId, string itemId, Stream content)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            Images[(catalogId, itemId)] = buffer.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string catalogId, string itemId) => Task.FromResult<Stream?>(
            Images.TryGetValue((catalogId, itemId), out var bytes) ? new MemoryStream(bytes) : null);

        public void Delete(string catalogId, string itemId) => Images.TryRemove((catalogId, itemId), out _);
    }

    private sealed class CaptureReportGenerator : IInsuranceReportGenerator
    {
        public bool ThrowOnGenerate { get; set; }
        public Resident? Resident { get; private set; }
        public IReadOnlyList<CatalogItem> Items { get; private set; } = [];

        public byte[] Generate(Resident resident, IReadOnlyList<CatalogItem> items)
        {
            if (ThrowOnGenerate)
                throw new InvalidOperationException("private storage credential");
            Resident = resident;
            Items = items;
            return Encoding.UTF8.GetBytes("%PDF-1.4\n%%EOF");
        }
    }
}
