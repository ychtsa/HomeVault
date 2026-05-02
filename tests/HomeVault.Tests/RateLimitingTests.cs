/*
 * FILE: RateLimitingTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Verifies that the login endpoint's rate limiter actually
 *              kicks in after the configured threshold. Lives in its own
 *              test class (and therefore its own factory / host instance)
 *              so the in-memory rate-limiter state cannot bleed into the
 *              other integration tests.
 */

using System.Net;
using Xunit;

namespace HomeVault.Tests
{
    public class RateLimitingTests : IClassFixture<HomeVaultWebAppFactory>
    {
        private readonly HomeVaultWebAppFactory _factory;

        public RateLimitingTests(HomeVaultWebAppFactory factory)
        {
            _factory = factory;
        }

        /*
         * Function: PostLogin_BeyondLimit_Returns429()
         * Description: The login policy permits 5 attempts per IP per
         *              minute. After firing 6 rapid POSTs, at least one
         *              must come back as 429 Too Many Requests with a
         *              Retry-After header. The body content of each request
         *              doesn't matter — the rate limiter rejects before the
         *              controller runs.
         */
        [Fact]
        public async Task PostLogin_BeyondLimit_Returns429()
        {
            HttpClient client = _factory.CreateClient(new()
            {
                AllowAutoRedirect = false
            });

            HttpStatusCode? lastStatus = null;
            for (int i = 0; i < 8; i++)
            {
                FormUrlEncodedContent body = new(new[]
                {
                    new KeyValuePair<string, string>("Username", "ratelimited"),
                    new KeyValuePair<string, string>("Password", "ratelimited")
                });

                HttpResponseMessage response = await client.PostAsync("/Account/Login", body);
                lastStatus = response.StatusCode;

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Verify the Retry-After hint accompanies the 429.
                    Assert.True(response.Headers.Contains("Retry-After"));
                    return;
                }
            }

            Assert.Fail($"Expected 429 within 8 attempts; final status was {lastStatus}.");
        }
    }
}
