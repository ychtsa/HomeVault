using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Api;

public sealed class LoginRequest
{
    [Required, StringLength(30)]
    public string Username { get; set; } = "";

    [Required, StringLength(100)]
    public string Password { get; set; } = "";
}

public sealed record UserResponse(
    string Username,
    string ResidentName,
    string ResidentAddress,
    string CatalogId);

public sealed record CsrfResponse(string Token);
