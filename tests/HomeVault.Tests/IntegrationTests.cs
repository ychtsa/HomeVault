/*
 * FILE: IntegrationTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: End-to-end HTTP tests that exercise the full ASP.NET Core
 *              pipeline: routing, authentication, security-headers
 *              middleware, anti-forgery, and health-check endpoint.
 */

using System.Net;
using Xunit;

namespace HomeVault.Tests
{
    public class IntegrationTests : IClassFixture<HomeVaultWebAppFactory>
    {
        private readonly HomeVaultWebAppFactory _factory;

        public IntegrationTests(HomeVaultWebAppFactory factory)
        {
            _factory = factory;
        }

        /*
         * Function: GetRoot_RedirectsToLogin_WhenUnauthenticated()
         * Description: Anonymous request to "/" must be intercepted by the
         *              cookie-auth middleware and redirected to the login
         *              page (because HomeController.Index has [Authorize]).
         */
        [Fact]
        public async Task GetRoot_RedirectsToLogin_WhenUnauthenticated()
        {
            HttpClient client = _factory.CreateClient(new()
            {
                AllowAutoRedirect = false
            });

            HttpResponseMessage response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }

        /*
         * Function: GetHealth_Returns200_WhenDbReachable()
         * Description: The /health endpoint must succeed when the DbContext
         *              health check can reach the (in-memory) database.
         */
        [Fact]
        public async Task GetHealth_Returns200_WhenDbReachable()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /*
         * Function: GetLogin_SetsAllSecurityHeaders()
         * Description: Verifies that SecurityHeadersMiddleware stamps every
         *              expected hardening header onto the response.
         */
        [Fact]
        public async Task GetLogin_SetsAllSecurityHeaders()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.GetAsync("/Account/Login");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(response.Headers.Contains("X-Frame-Options"),
                "X-Frame-Options header is missing.");
            Assert.Equal("DENY",
                response.Headers.GetValues("X-Frame-Options").Single());

            Assert.True(response.Headers.Contains("X-Content-Type-Options"),
                "X-Content-Type-Options header is missing.");
            Assert.Equal("nosniff",
                response.Headers.GetValues("X-Content-Type-Options").Single());

            Assert.True(response.Headers.Contains("Referrer-Policy"),
                "Referrer-Policy header is missing.");
            Assert.True(response.Headers.Contains("Permissions-Policy"),
                "Permissions-Policy header is missing.");
            Assert.True(response.Headers.Contains("Content-Security-Policy"),
                "Content-Security-Policy header is missing.");
        }

        /*
         * Function: PostLogin_WithoutAntiforgeryToken_IsRejected()
         * Description: Submitting credentials without the anti-forgery token
         *              must not be processed by the controller. ASP.NET Core
         *              returns 400 (Bad Request) for missing tokens.
         */
        [Fact]
        public async Task PostLogin_WithoutAntiforgeryToken_IsRejected()
        {
            HttpClient client = _factory.CreateClient(new()
            {
                AllowAutoRedirect = false
            });

            FormUrlEncodedContent body = new(new[]
            {
                new KeyValuePair<string, string>("Username", "anyone"),
                new KeyValuePair<string, string>("Password", "irrelevant")
            });

            HttpResponseMessage response = await client.PostAsync("/Account/Login", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /*
         * Function: GetLogin_RendersAntiforgeryTokenInForm()
         * Description: Confirms the rendered HTML embeds the
         *              __RequestVerificationToken hidden input — without it,
         *              the form would be unable to satisfy the
         *              [ValidateAntiForgeryToken] attribute.
         */
        [Fact]
        public async Task GetLogin_RendersAntiforgeryTokenInForm()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.GetAsync("/Account/Login");
            string html = await response.Content.ReadAsStringAsync();

            Assert.Contains("__RequestVerificationToken", html);
        }
    }
}
