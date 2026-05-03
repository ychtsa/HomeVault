/*
 * FILE: AccountController.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Handles login, logout, signup, and password recovery for
 *              HomeVault. Uses EF Core for data access, BCrypt for password
 *              hashing and verification, cookie-based forms authentication
 *              with ResidentId and CatalogId stored as claims, and a SHA-256
 *              token hash for password-reset links.
 */

using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using HomeVault.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace HomeVault.Controllers
{
    public class AccountController : Controller
    {
        private const int ResetTokenLifetimeMinutes = 60;

        private readonly CatalogDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AccountController> _logger;

        public AccountController(CatalogDbContext context,
                                 IEmailSender emailSender,
                                 ILogger<AccountController> logger)
        {
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ===== LOGIN =====================================================

        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            LoginViewModel model = new LoginViewModel { ReturnUrl = returnUrl };
            return View(model);
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            ResidentUser? user = await _context.ResidentUsers
                .Include(u => u.Resident)
                .FirstOrDefaultAsync(u => u.Username == model.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                _logger.LogWarning(
                    "Failed login attempt for {Username} from {RemoteIp}",
                    model.Username,
                    HttpContext.Connection.RemoteIpAddress);
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("ResidentId", user.ResidentId),
                new Claim("CatalogId", user.Resident.CatalogId)
            };

            ClaimsIdentity identity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);
            AuthenticationProperties authProps = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                authProps);

            _logger.LogInformation("User logged in: {Username}", user.Username);

            string redirectUrl = model.ReturnUrl ?? Url.Content("~/");
            return LocalRedirect(redirectUrl);
        }

        // ===== LOGOUT ====================================================

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ===== SIGNUP ====================================================

        [HttpGet, AllowAnonymous]
        public IActionResult Signup() => View(new SignupViewModel());

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Signup(SignupViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            bool usernameTaken = await _context.ResidentUsers
                .AnyAsync(u => u.Username == model.Username);
            if (usernameTaken)
            {
                ModelState.AddModelError(nameof(model.Username), "Username already exists.");
                return View(model);
            }

            bool emailTaken = await _context.ResidentUsers
                .AnyAsync(u => u.Email == model.Email);
            if (emailTaken)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with that email already exists.");
                return View(model);
            }

            string residentId = Guid.NewGuid().ToString("N").Substring(0, 5);
            string catalogId = Guid.NewGuid().ToString("N").Substring(0, 5);

            Catalog catalog = new Catalog { CatalogId = catalogId };
            Resident resident = new Resident
            {
                ResidentId = residentId,
                ResidentName = model.ResidentName,
                ResidentAddress = model.ResidentAddress,
                CatalogId = catalogId
            };
            ResidentUser user = new ResidentUser
            {
                ResidentId = residentId,
                Username = model.Username,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password)
            };

            _context.Catalogs.Add(catalog);
            _context.Residents.Add(resident);
            _context.ResidentUsers.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user registered: {Username}", model.Username);
            TempData["SuccessMessage"] = "Account created. Please log in.";
            return RedirectToAction("Login");
        }

        // ===== FORGOT PASSWORD ===========================================

        [HttpGet, AllowAnonymous]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        [EnableRateLimiting("forgotPassword")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            ResidentUser? user = await _context.ResidentUsers
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            // Only act if the email actually maps to an account, but ALWAYS
            // show the same confirmation page either way — this prevents
            // attackers from probing which emails are registered.
            if (user != null)
            {
                string token = GenerateToken();
                user.PasswordResetTokenHash = HashToken(token);
                user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(ResetTokenLifetimeMinutes);
                await _context.SaveChangesAsync();

                string resetUrl = Url.Action(
                    action: nameof(ResetPassword),
                    controller: "Account",
                    values: new { token },
                    protocol: Request.Scheme)!;

                string body =
                    $"Someone (hopefully you) requested a password reset for your HomeVault account.\n\n" +
                    $"To choose a new password, follow this link within {ResetTokenLifetimeMinutes} minutes:\n" +
                    $"{resetUrl}\n\n" +
                    "If you didn't request this, you can ignore the email — your password won't change.";

                await _emailSender.SendAsync(user.Email, "Reset your HomeVault password", body);

                _logger.LogInformation("Password reset issued for {Username}", user.Username);
            }
            else
            {
                _logger.LogInformation(
                    "Password reset requested for unknown email from {RemoteIp}",
                    HttpContext.Connection.RemoteIpAddress);
            }

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet, AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation() => View();

        // ===== RESET PASSWORD ============================================

        [HttpGet, AllowAnonymous]
        public IActionResult ResetPassword(string? token = null)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("A reset token is required.");

            return View(new ResetPasswordViewModel { Token = token });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            string tokenHash = HashToken(model.Token);
            DateTime now = DateTime.UtcNow;

            ResidentUser? user = await _context.ResidentUsers
                .FirstOrDefaultAsync(u =>
                    u.PasswordResetTokenHash == tokenHash &&
                    u.PasswordResetTokenExpiresAt != null &&
                    u.PasswordResetTokenExpiresAt > now);

            if (user == null)
            {
                ModelState.AddModelError("", "This reset link is invalid or has expired.");
                return View(model);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiresAt = null;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password reset completed for {Username}", user.Username);

            TempData["SuccessMessage"] = "Password updated. Please log in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        // ===== TOKEN HELPERS =============================================

        /*
         * Function: GenerateToken()
         * Description: Generates 32 cryptographically random bytes encoded
         *              as URL-safe Base64 (~43 chars). Long enough to make
         *              brute-force enumeration infeasible.
         * Return: string - the URL-safe token to embed in the reset link.
         */
        private static string GenerateToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return WebEncoders.Base64UrlEncode(bytes);
        }

        /*
         * Function: HashToken(string token)
         * Description: SHA-256 hex digest of the token. The DB stores only
         *              the digest, so a database leak alone does not expose
         *              any usable reset tokens.
         * Parameter: string token - the URL-safe token from the reset link.
         * Return: string - 64-char lowercase hex SHA-256 digest.
         */
        private static string HashToken(string token)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
