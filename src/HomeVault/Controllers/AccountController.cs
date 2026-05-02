/*
 * FILE: AccountController.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Handles login, logout, and self-service signup for HomeVault.
 *              Uses EF Core for data access, BCrypt for password hashing
 *              and verification, and cookie-based forms authentication with
 *              ResidentId and CatalogId stored as claims.
 */

using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HomeVault.Controllers
{
    public class AccountController : Controller
    {
        private readonly CatalogDbContext _context;
        private readonly ILogger<AccountController> _logger;

        /*
         * Function: AccountController(CatalogDbContext context,
         *                             ILogger<AccountController> logger)
         * Description: Constructor. Receives the EF context and logger via DI.
         * Parameter: CatalogDbContext context - the EF Core context.
         * Parameter: ILogger<AccountController> logger - logger instance.
         * Return: none (constructor).
         */
        public AccountController(CatalogDbContext context,
                                 ILogger<AccountController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /*
         * Function: Login(string returnUrl) [GET]
         * Description: Renders the login form.
         * Parameter: string returnUrl - URL to redirect to after success.
         * Return: IActionResult result - the Login view.
         */
        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            LoginViewModel model = new LoginViewModel { ReturnUrl = returnUrl };
            IActionResult result = View(model);
            return result;
        }

        /*
         * Function: Login(LoginViewModel model) [POST]
         * Description: Looks up the user by username, verifies the password
         *              with BCrypt, and on success issues an auth cookie
         *              carrying Username, ResidentId, and CatalogId claims.
         * Parameter: LoginViewModel model - submitted credentials.
         * Return: IActionResult result - LocalRedirect on success, or the
         *         Login view repopulated with errors on failure.
         */
        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            IActionResult result = View(model);

            if (ModelState.IsValid)
            {
                ResidentUser? user = await _context.ResidentUsers
                    .Include(u => u.Resident)
                    .FirstOrDefaultAsync(u => u.Username == model.Username);

                if (user != null && BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
                {
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
                    result = LocalRedirect(redirectUrl);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed login attempt for {Username} from {RemoteIp}",
                        model.Username,
                        HttpContext.Connection.RemoteIpAddress);
                    ModelState.AddModelError("", "Invalid username or password.");
                }
            }

            return result;
        }

        /*
         * Function: Logout() [POST]
         * Description: Clears the auth cookie and redirects to the login page.
         * Parameter: none.
         * Return: IActionResult result - redirect to Account/Login.
         */
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            IActionResult result = RedirectToAction("Login");
            return result;
        }

        /*
         * Function: Signup() [GET]
         * Description: Renders the signup form.
         * Parameter: none.
         * Return: IActionResult result - the Signup view.
         */
        [HttpGet, AllowAnonymous]
        public IActionResult Signup()
        {
            IActionResult result = View(new SignupViewModel());
            return result;
        }

        /*
         * Function: Signup(SignupViewModel model) [POST]
         * Description: Creates a Catalog, Resident, and ResidentUser inside
         *              one EF SaveChanges call so the three rows commit
         *              atomically. Hashes the password with BCrypt before
         *              persisting it.
         * Parameter: SignupViewModel model - submitted form data.
         * Return: IActionResult result - redirect to Login on success, or
         *         the Signup view with errors on failure.
         */
        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Signup(SignupViewModel model)
        {
            IActionResult result = View(model);

            if (ModelState.IsValid)
            {
                bool usernameTaken = await _context.ResidentUsers
                    .AnyAsync(u => u.Username == model.Username);

                if (usernameTaken)
                {
                    ModelState.AddModelError(nameof(model.Username), "Username already exists.");
                }
                else
                {
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
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password)
                    };

                    _context.Catalogs.Add(catalog);
                    _context.Residents.Add(resident);
                    _context.ResidentUsers.Add(user);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("New user registered: {Username}", model.Username);
                    TempData["SuccessMessage"] = "Account created. Please log in.";
                    result = RedirectToAction("Login");
                }
            }

            return result;
        }
    }
}