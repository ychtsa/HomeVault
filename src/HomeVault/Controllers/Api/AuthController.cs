using System.Security.Claims;
using HomeVault.Data;
using HomeVault.Models.Api;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Controllers.Api;

[ApiController]
[Route("api/auth")]
[Authorize]
[AutoValidateAntiforgeryToken]
[Produces("application/json")]
public sealed class AuthController(CatalogDbContext context, IAntiforgery antiforgery) : ControllerBase
{
    private const int MaxFailedLoginAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Get a token to send in X-CSRF-TOKEN on all POST, PUT and DELETE requests.</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CsrfResponse), StatusCodes.Status200OK)]
    public ActionResult<CsrfResponse> Csrf() =>
        Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!));

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        string? residentId = User.FindFirstValue("ResidentId");
        string? catalogId = User.FindFirstValue("CatalogId");
        ResidentUser? user = await context.ResidentUsers.AsNoTracking()
            .Include(u => u.Resident)
            .SingleOrDefaultAsync(u => u.ResidentId == residentId &&
                u.Resident.CatalogId == catalogId, cancellationToken);

        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        ResidentUser? user = await context.ResidentUsers.Include(u => u.Resident)
            .SingleOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user is null)
            return InvalidCredentials();

        if (user.LockedUntil <= DateTime.UtcNow)
            user.LockedUntil = null;

        bool passwordCorrect = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (user.LockedUntil is not null)
        {
            // Reveal lockout details only after the account password has been verified.
            if (!passwordCorrect)
                return InvalidCredentials();

            int minutes = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return Problem(statusCode: StatusCodes.Status423Locked, title: "Account temporarily locked",
                detail: $"Too many failed sign-in attempts. Try again in {minutes} minute(s).");
        }

        if (!passwordCorrect)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
            }

            await context.SaveChangesAsync(cancellationToken);
            return InvalidCredentials();
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await context.SaveChangesAsync(cancellationToken);

        Claim[] claims =
        [
            new(ClaimTypes.Name, user.Username),
            new("ResidentId", user.ResidentId),
            new("CatalogId", user.Resident.CatalogId)
        ];
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        return Ok(ToResponse(user));
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserResponse>> Signup(SignupViewModel request, CancellationToken cancellationToken)
    {
        if (await context.ResidentUsers.AnyAsync(u => u.Username == request.Username || u.Email == request.Email,
            cancellationToken))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Account already exists",
                detail: "That username or email is already registered.");

        // Keep the existing five-character schema and avoid already allocated identifiers.
        string residentId;
        do { residentId = Guid.NewGuid().ToString("N")[..5]; }
        while (await context.Residents.AnyAsync(r => r.ResidentId == residentId, cancellationToken));
        string catalogId;
        do { catalogId = Guid.NewGuid().ToString("N")[..5]; }
        while (await context.Catalogs.AnyAsync(c => c.CatalogId == catalogId, cancellationToken));

        var catalog = new Catalog { CatalogId = catalogId };
        var resident = new Resident
        {
            ResidentId = residentId,
            ResidentName = request.ResidentName,
            ResidentAddress = request.ResidentAddress,
            CatalogId = catalogId,
            Catalog = catalog
        };
        var user = new ResidentUser
        {
            ResidentId = residentId,
            Resident = resident,
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        context.ResidentUsers.Add(user);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // SQL's unique constraints also protect concurrent registrations
            // that both passed the existence check above.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Account already exists",
                detail: "That username or email is already registered. Please try another.");
        }
        return StatusCode(StatusCodes.Status201Created, ToResponse(user));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private ObjectResult InvalidCredentials() => Problem(statusCode: StatusCodes.Status401Unauthorized,
        title: "Sign-in failed", detail: "Invalid username or password.");

    private static UserResponse ToResponse(ResidentUser user) =>
        new(user.Username, user.Resident.ResidentName, user.Resident.ResidentAddress, user.Resident.CatalogId);
}
