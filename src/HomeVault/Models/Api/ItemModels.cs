using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Api;

public sealed class ItemRequest
{
    [Required, StringLength(30)]
    public string ItemName { get; set; } = "";

    [Required, StringLength(30)]
    public string ItemType { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int ItemValue { get; set; }
}

public sealed record ItemResponse(
    string ItemId,
    string ItemName,
    string ItemType,
    int ItemValue,
    string? ImageUrl);

public sealed class ImageUploadRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}
