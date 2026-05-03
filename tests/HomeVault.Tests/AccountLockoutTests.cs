/*
 * FILE: AccountLockoutTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Verifies the per-account lockout layered on top of the
 *              per-IP rate limiter: failure-count accumulation, lockout
 *              after the threshold, rejection of correct passwords during
 *              lockout, auto-expiry, and counter reset on success.
 */

using HomeVault.Controllers;
using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using HomeVault.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HomeVault.Tests
{
    public class AccountLockoutTests
    {
        private const int LockoutThreshold = 10;
        private const string Username = "alice";
        private const string CorrectPassword = "Correct1!";
        private const string WrongPassword = "Wrong1!";

        /*
         * Class: TestRig
         * Description: Wires up an AccountController against a fresh
         *              in-memory database that contains a single user
         *              (alice / Correct1!), with a stub authentication
         *              service so SignInAsync does not fall over.
         */
        private class TestRig
        {
            public CatalogDbContext Context { get; }
            public AccountController Controller { get; }

            public TestRig()
            {
                DbContextOptions<CatalogDbContext> options =
                    new DbContextOptionsBuilder<CatalogDbContext>()
                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                        .Options;
                Context = new CatalogDbContext(options);
                SeedAlice();

                ILogger<AccountController> logger = new Mock<ILogger<AccountController>>().Object;
                IEmailSender email = new Mock<IEmailSender>().Object;
                Controller = new AccountController(Context, email, logger);

                ServiceCollection services = new();
                Mock<IAuthenticationService> auth = new();
                auth.Setup(a => a.SignInAsync(
                        It.IsAny<HttpContext>(),
                        It.IsAny<string>(),
                        It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                        It.IsAny<AuthenticationProperties>()))
                    .Returns(Task.CompletedTask);
                services.AddSingleton(auth.Object);

                DefaultHttpContext http = new()
                {
                    RequestServices = services.BuildServiceProvider()
                };
                http.Features.Set<IHttpRequestFeature>(new HttpRequestFeature
                {
                    Scheme = "https",
                    Method = "POST",
                    Path = "/Account/Login"
                });

                Controller.ControllerContext = new ControllerContext { HttpContext = http };
                Controller.TempData = new TempDataDictionary(http, new Mock<ITempDataProvider>().Object);
            }

            private void SeedAlice()
            {
                Context.Catalogs.Add(new Catalog { CatalogId = "catA" });
                Context.Residents.Add(new Resident
                {
                    ResidentId = "rA",
                    ResidentName = "Alice",
                    ResidentAddress = "1 Main St",
                    CatalogId = "catA"
                });
                Context.ResidentUsers.Add(new ResidentUser
                {
                    ResidentId = "rA",
                    Username = Username,
                    Email = "alice@example.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(CorrectPassword)
                });
                Context.SaveChanges();
            }
        }

        // ReturnUrl is supplied explicitly so the success path doesn't call
        // Url.Content("~/") (which would need a fully configured URL helper).
        private static LoginViewModel Bad() =>
            new() { Username = Username, Password = WrongPassword, ReturnUrl = "/" };
        private static LoginViewModel Good() =>
            new() { Username = Username, Password = CorrectPassword, ReturnUrl = "/" };

        // ===== Counter behaviour =========================================

        [Fact]
        public async Task FailedLogin_IncrementsFailureCounter()
        {
            TestRig rig = new();

            await rig.Controller.Login(Bad());

            ResidentUser alice = rig.Context.ResidentUsers.Single();
            Assert.Equal(1, alice.FailedLoginAttempts);
            Assert.Null(alice.LockedUntil);
        }

        [Fact]
        public async Task SuccessfulLogin_ResetsFailureCounter()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            alice.FailedLoginAttempts = 5;
            rig.Context.SaveChanges();

            await rig.Controller.Login(Good());

            rig.Context.Entry(alice).Reload();
            Assert.Equal(0, alice.FailedLoginAttempts);
        }

        // ===== Lockout activation ========================================

        [Fact]
        public async Task FailedLogins_BeyondThreshold_LockTheAccount()
        {
            TestRig rig = new();

            for (int i = 0; i < LockoutThreshold; i++)
            {
                rig.Controller.ModelState.Clear();
                await rig.Controller.Login(Bad());
            }

            ResidentUser alice = rig.Context.ResidentUsers.Single();
            Assert.NotNull(alice.LockedUntil);
            Assert.True(alice.LockedUntil > DateTime.UtcNow);
            Assert.Equal(0, alice.FailedLoginAttempts); // counter reset on lockout
        }

        // ===== Behaviour while locked ====================================

        [Fact]
        public async Task LockedAccount_RejectsCorrectPasswordWithLockoutMessage()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            alice.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            rig.Context.SaveChanges();

            IActionResult result = await rig.Controller.Login(Good());

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);
            string errorText = string.Join(" ",
                rig.Controller.ModelState[""]!.Errors.Select(e => e.ErrorMessage));
            Assert.Contains("locked", errorText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LockedAccount_RejectsWrongPasswordWithGenericMessage()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            alice.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            rig.Context.SaveChanges();

            IActionResult result = await rig.Controller.Login(Bad());

            Assert.IsType<ViewResult>(result);
            string errorText = string.Join(" ",
                rig.Controller.ModelState[""]!.Errors.Select(e => e.ErrorMessage));
            // No mention of lockout to wrong-password attempts — preserves
            // the no-account-enumeration property for failed attackers.
            Assert.DoesNotContain("locked", errorText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Invalid", errorText, StringComparison.OrdinalIgnoreCase);
        }

        // ===== Auto-expiry ===============================================

        [Fact]
        public async Task LockoutAutoExpires_AndAcceptsCorrectPasswordAfterwards()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            alice.LockedUntil = DateTime.UtcNow.AddMinutes(-1); // already past
            rig.Context.SaveChanges();

            IActionResult result = await rig.Controller.Login(Good());

            // Successful login redirects (LocalRedirect).
            Assert.IsAssignableFrom<IActionResult>(result);
            Assert.True(rig.Controller.ModelState.IsValid);

            rig.Context.Entry(alice).Reload();
            Assert.Null(alice.LockedUntil);
            Assert.Equal(0, alice.FailedLoginAttempts);
        }
    }
}
