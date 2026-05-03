/*
 * FILE: PasswordResetTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Verifies the forgot-password / reset-password flow:
 *              email-enumeration prevention, token hashing, expiry,
 *              single-use semantics, and password rotation.
 */

using System.Security.Cryptography;
using System.Text;
using HomeVault.Controllers;
using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using HomeVault.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HomeVault.Tests
{
    public class PasswordResetTests
    {
        private const string AliceEmail = "alice@example.com";
        private const string AliceOldPassword = "OldPass1!";

        /*
         * Class: TestRig
         * Description: Builds an isolated AccountController bound to a fresh
         *              in-memory database that already contains Alice.
         *              Every test creates its own rig.
         */
        private class TestRig
        {
            public CatalogDbContext Context { get; }
            public AccountController Controller { get; }
            public Mock<IEmailSender> EmailSender { get; } = new();

            public TestRig()
            {
                DbContextOptions<CatalogDbContext> options =
                    new DbContextOptionsBuilder<CatalogDbContext>()
                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                        .Options;
                Context = new CatalogDbContext(options);
                SeedAlice();

                ILogger<AccountController> logger = new Mock<ILogger<AccountController>>().Object;
                Controller = new AccountController(Context, EmailSender.Object, logger);

                DefaultHttpContext http = new DefaultHttpContext();
                Controller.ControllerContext = new ControllerContext { HttpContext = http };
                Controller.TempData = new TempDataDictionary(http, new Mock<ITempDataProvider>().Object);
                Controller.Url = BuildUrlHelper();
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
                    Username = "alice",
                    Email = AliceEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(AliceOldPassword)
                });
                Context.SaveChanges();
            }

            private static IUrlHelper BuildUrlHelper()
            {
                Mock<IUrlHelper> mock = new();
                mock.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
                    .Returns<UrlActionContext>(ctx =>
                    {
                        RouteValueDictionary values = new(ctx.Values ?? new { });
                        return $"https://test/{ctx.Controller}/{ctx.Action}?token={values["token"]}";
                    });
                return mock.Object;
            }
        }

        /*
         * Function: Sha256Hex(string text)
         * Description: Replicates the SHA-256 hex encoding used by the
         *              controller so tests can assert on the stored hash.
         */
        private static string Sha256Hex(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            StringBuilder sb = new(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ===== ForgotPassword ============================================

        [Fact]
        public async Task ForgotPassword_WithKnownEmail_StoresHashedTokenAndSendsEmail()
        {
            TestRig rig = new();

            ForgotPasswordViewModel model = new() { Email = AliceEmail };
            IActionResult result = await rig.Controller.ForgotPassword(model);

            RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AccountController.ForgotPasswordConfirmation), redirect.ActionName);

            ResidentUser alice = rig.Context.ResidentUsers.Single(u => u.Username == "alice");
            Assert.NotNull(alice.PasswordResetTokenHash);
            Assert.Equal(64, alice.PasswordResetTokenHash!.Length);   // SHA-256 hex
            Assert.NotNull(alice.PasswordResetTokenExpiresAt);
            Assert.True(alice.PasswordResetTokenExpiresAt > DateTime.UtcNow);

            rig.EmailSender.Verify(s => s.SendAsync(
                AliceEmail,
                It.IsAny<string>(),
                It.Is<string>(body => body.Contains("token="))), Times.Once);
        }

        [Fact]
        public async Task ForgotPassword_WithUnknownEmail_DoesNotSendEmail_ButReturnsSameConfirmation()
        {
            TestRig rig = new();

            ForgotPasswordViewModel model = new() { Email = "ghost@example.com" };
            IActionResult result = await rig.Controller.ForgotPassword(model);

            RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AccountController.ForgotPasswordConfirmation), redirect.ActionName);

            rig.EmailSender.Verify(
                s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        // ===== ResetPassword =============================================

        [Fact]
        public async Task ResetPassword_WithValidToken_RotatesPasswordAndClearsToken()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            string oldHash = alice.PasswordHash;

            const string token = "valid-token-12345";
            alice.PasswordResetTokenHash = Sha256Hex(token);
            alice.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
            rig.Context.SaveChanges();

            ResetPasswordViewModel model = new()
            {
                Token = token,
                Password = "BrandNew1!",
                ConfirmPassword = "BrandNew1!"
            };
            IActionResult result = await rig.Controller.ResetPassword(model);

            RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AccountController.Login), redirect.ActionName);

            rig.Context.Entry(alice).Reload();
            Assert.NotEqual(oldHash, alice.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("BrandNew1!", alice.PasswordHash));
            Assert.Null(alice.PasswordResetTokenHash);
            Assert.Null(alice.PasswordResetTokenExpiresAt);
        }

        [Fact]
        public async Task ResetPassword_WithInvalidToken_AddsModelErrorAndKeepsPassword()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            string oldHash = alice.PasswordHash;

            ResetPasswordViewModel model = new()
            {
                Token = "definitely-not-a-real-token",
                Password = "Whatever1!",
                ConfirmPassword = "Whatever1!"
            };
            IActionResult result = await rig.Controller.ResetPassword(model);

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);

            rig.Context.Entry(alice).Reload();
            Assert.Equal(oldHash, alice.PasswordHash);
        }

        [Fact]
        public async Task ResetPassword_WithExpiredToken_AddsModelErrorAndKeepsPassword()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();
            string oldHash = alice.PasswordHash;

            const string token = "expired-token";
            alice.PasswordResetTokenHash = Sha256Hex(token);
            alice.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1); // already past
            rig.Context.SaveChanges();

            ResetPasswordViewModel model = new()
            {
                Token = token,
                Password = "Whatever1!",
                ConfirmPassword = "Whatever1!"
            };
            IActionResult result = await rig.Controller.ResetPassword(model);

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);

            rig.Context.Entry(alice).Reload();
            Assert.Equal(oldHash, alice.PasswordHash);
            // Expired token must remain rejected (controller does not auto-clear it).
        }

        [Fact]
        public async Task ResetPassword_TokenIsSingleUse_SecondAttemptFails()
        {
            TestRig rig = new();
            ResidentUser alice = rig.Context.ResidentUsers.Single();

            const string token = "one-use-token";
            alice.PasswordResetTokenHash = Sha256Hex(token);
            alice.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
            rig.Context.SaveChanges();

            ResetPasswordViewModel first = new()
            {
                Token = token,
                Password = "FirstPass1!",
                ConfirmPassword = "FirstPass1!"
            };
            await rig.Controller.ResetPassword(first);

            // Reset ModelState because the same controller instance reused.
            rig.Controller.ModelState.Clear();

            ResetPasswordViewModel second = new()
            {
                Token = token,
                Password = "SecondPass1!",
                ConfirmPassword = "SecondPass1!"
            };
            IActionResult result = await rig.Controller.ResetPassword(second);

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);

            rig.Context.Entry(alice).Reload();
            Assert.True(BCrypt.Net.BCrypt.Verify("FirstPass1!", alice.PasswordHash));
            Assert.False(BCrypt.Net.BCrypt.Verify("SecondPass1!", alice.PasswordHash));
        }
    }
}
