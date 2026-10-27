using HomeVault.Data;
using HomeVault.Models.Api;
using HomeVault.Models.Entities;
using HomeVault.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Controllers.Api;

[ApiController]
[Route("api/items")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class ItemsApiController(
    CatalogDbContext context,
    ICatalogImageStorage imageStorage,
    IInsuranceReportGenerator reportGenerator) : ControllerBase
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private string CurrentCatalogId => User.FindFirst("CatalogId")?.Value ?? "";

    // Scoping the query, rather than checking an item after loading it, keeps
    // every operation (including media and reports) within the user's catalog.
    private IQueryable<CatalogItem> CatalogItems =>
        context.CatalogItems.Where(i => i.CatalogId == CurrentCatalogId);

    [HttpGet]
    [ProducesResponseType(typeof(ItemResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ItemResponse>>> List(CancellationToken cancellationToken)
    {
        var items = await CatalogItems.AsNoTracking().OrderBy(i => i.ItemName).ToListAsync(cancellationToken);
        return Ok(items.Select(ToResponse).ToArray());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemResponse>> Get(string id, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.AsNoTracking().SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ItemResponse>> Create(ItemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(CurrentCatalogId))
            return Forbid();

        string itemId;
        do { itemId = Guid.NewGuid().ToString("N")[..5]; }
        while (await context.CatalogItems.AnyAsync(i => i.ItemId == itemId, cancellationToken));

        var item = new CatalogItem
        {
            ItemId = itemId,
            CatalogId = CurrentCatalogId,
            ItemName = request.ItemName,
            ItemType = request.ItemType,
            ItemValue = request.ItemValue
        };
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = item.ItemId }, ToResponse(item));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemResponse>> Update(string id, ItemRequest request, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        if (item is null)
            return NotFound();

        item.ItemName = request.ItemName;
        item.ItemType = request.ItemType;
        item.ItemValue = request.ItemValue;
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(item));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        if (item is null)
            return NotFound();

        if (item.ImageContentType is not null)
            imageStorage.Delete(item.CatalogId, item.ItemId);
        context.CatalogItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id}/image")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(string id, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.AsNoTracking().SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        if (item?.ImageContentType is null)
            return NotFound();

        Stream? stream = await imageStorage.OpenReadAsync(item.CatalogId, item.ItemId);
        return stream is null ? NotFound() : File(stream, item.ImageContentType);
    }

    [HttpPost("{id}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 64 * 1024)]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ItemResponse>> UploadImage(
        string id, [FromForm] ImageUploadRequest upload, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        if (item is null)
            return NotFound();

        IFormFile file = upload.File;
        if (file.Length is <= 0 or > MaxImageBytes)
            return InvalidImage("Choose a nonempty image of 5 MB or smaller.");

        string contentType = file.ContentType.ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            return InvalidImage("Only JPEG, PNG, or WebP images are accepted.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (!HasImageSignature(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), contentType))
            return InvalidImage("The file contents do not match the image format.");

        buffer.Position = 0;
        await imageStorage.SaveAsync(item.CatalogId, item.ItemId, buffer);
        item.ImageContentType = contentType;
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(item));
    }

    [HttpDelete("{id}/image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(string id, CancellationToken cancellationToken)
    {
        var item = await CatalogItems.SingleOrDefaultAsync(i => i.ItemId == id, cancellationToken);
        if (item is null)
            return NotFound();

        imageStorage.Delete(item.CatalogId, item.ItemId);
        item.ImageContentType = null;
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("report")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(CancellationToken cancellationToken)
    {
        var resident = await context.Residents.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CatalogId == CurrentCatalogId, cancellationToken);
        if (resident is null)
            return NotFound();

        var items = await CatalogItems.AsNoTracking().OrderBy(i => i.ItemName).ToListAsync(cancellationToken);
        return File(reportGenerator.Generate(resident, items), "application/pdf",
            $"homevault-inventory-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
    }

    private ItemResponse ToResponse(CatalogItem item) =>
        new(item.ItemId, item.ItemName, item.ItemType, item.ItemValue,
            item.ImageContentType is null ? null : Url.Action(nameof(Image), new { id = item.ItemId }));

    private ActionResult InvalidImage(string message)
    {
        ModelState.AddModelError("file", message);
        return ValidationProblem(ModelState);
    }

    private static bool HasImageSignature(ReadOnlySpan<byte> bytes, string contentType) => contentType switch
    {
        "image/png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
