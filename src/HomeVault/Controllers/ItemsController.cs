/*
 * FILE: ItemsController.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: CRUD controller for catalog items, scoped at the database
 *              level by the CatalogId stored in the signed-in user's
 *              cookie claims. Every read and write is filtered by that
 *              claim, which prevents one user from seeing or modifying
 *              another user's items even by guessing IDs. Item photos
 *              are served via an authorized action (not a static URL),
 *              applying the same isolation guarantee to image bytes.
 */

using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using HomeVault.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Controllers
{
    [Authorize]
    public class ItemsController : Controller
    {
        // Allow-list for uploaded photo content types.
        private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        private const long MaxImageBytes = 5 * 1024 * 1024;   // 5 MB

        private readonly CatalogDbContext _context;
        private readonly ICatalogImageStorage _imageStorage;
        private readonly IInsuranceReportGenerator _reportGenerator;

        public ItemsController(
            CatalogDbContext context,
            ICatalogImageStorage imageStorage,
            IInsuranceReportGenerator reportGenerator)
        {
            _context = context;
            _imageStorage = imageStorage;
            _reportGenerator = reportGenerator;
        }

        /*
         * Property: CurrentCatalogId
         * Description: Reads the current user's CatalogId from claims so
         *              that every query in this controller can scope to it.
         * Return: string - the CatalogId, or "" if no claim is present.
         */
        private string CurrentCatalogId =>
            User.FindFirst("CatalogId")?.Value ?? "";

        // ===== INDEX =====================================================

        public async Task<IActionResult> Index()
        {
            List<CatalogItem> items = await _context.CatalogItems
                .Where(i => i.CatalogId == CurrentCatalogId)
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            return View(items);
        }

        // ===== INSURANCE REPORT (PDF) ====================================

        /*
         * Function: DownloadReport() [GET]
         * Description: Streams a freshly-rendered PDF inventory of every
         *              item in the signed-in user's catalog. Suitable for
         *              handing to an insurer.
         * Return: IActionResult - File result with the PDF bytes.
         */
        [HttpGet]
        public async Task<IActionResult> DownloadReport()
        {
            Resident? resident = await _context.Residents
                .FirstOrDefaultAsync(r => r.CatalogId == CurrentCatalogId);
            if (resident == null) return NotFound();

            List<CatalogItem> items = await _context.CatalogItems
                .Where(i => i.CatalogId == CurrentCatalogId)
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            byte[] pdf = _reportGenerator.Generate(resident, items);
            string filename = $"homevault-inventory-{DateTime.UtcNow:yyyy-MM-dd}.pdf";
            return File(pdf, "application/pdf", filename);
        }

        // ===== IMAGE (authorized static-like serving) ====================

        /*
         * Function: Image(string id) [GET]
         * Description: Streams the item's stored photo back to the browser.
         *              Filtered by CatalogId — guessing another user's
         *              ItemId resolves to NotFound, never a leaked image.
         * Parameter: string id - the ItemId.
         * Return: IActionResult - File result with the correct media type,
         *         or NotFound() if the item or image is missing.
         */
        [HttpGet]
        public async Task<IActionResult> Image(string id)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);

            if (item == null || string.IsNullOrEmpty(item.ImageContentType))
                return NotFound();

            Stream? stream = await _imageStorage.OpenReadAsync(item.CatalogId, item.ItemId);
            if (stream == null) return NotFound();

            return File(stream, item.ImageContentType);
        }

        // ===== CREATE ====================================================

        [HttpGet]
        public IActionResult Create() => View(new CatalogItemFormViewModel());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CatalogItemFormViewModel model)
        {
            ValidateUploadedImage(model.Image);
            if (!ModelState.IsValid) return View(model);

            CatalogItem item = new CatalogItem
            {
                ItemId = Guid.NewGuid().ToString("N").Substring(0, 5),
                CatalogId = CurrentCatalogId,
                ItemName = model.ItemName,
                ItemType = model.ItemType,
                ItemValue = model.ItemValue
            };

            if (model.Image is { Length: > 0 })
            {
                await using Stream stream = model.Image.OpenReadStream();
                await _imageStorage.SaveAsync(item.CatalogId, item.ItemId, stream);
                item.ImageContentType = model.Image.ContentType;
            }

            _context.CatalogItems.Add(item);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"\"{item.ItemName}\" was added.";
            return RedirectToAction(nameof(Index));
        }

        // ===== EDIT ======================================================

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);

            if (item == null) return NotFound();

            CatalogItemFormViewModel model = new CatalogItemFormViewModel
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                ItemType = item.ItemType,
                ItemValue = item.ItemValue,
                HasExistingImage = !string.IsNullOrEmpty(item.ImageContentType)
            };
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CatalogItemFormViewModel model)
        {
            ValidateUploadedImage(model.Image);
            if (!ModelState.IsValid || model.ItemId == null)
            {
                model.HasExistingImage = await _context.CatalogItems
                    .AnyAsync(i => i.ItemId == model.ItemId
                                   && i.CatalogId == CurrentCatalogId
                                   && i.ImageContentType != null);
                return View(model);
            }

            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == model.ItemId
                                          && i.CatalogId == CurrentCatalogId);
            if (item == null) return NotFound();

            item.ItemName = model.ItemName;
            item.ItemType = model.ItemType;
            item.ItemValue = model.ItemValue;

            if (model.Image is { Length: > 0 })
            {
                // New upload replaces any previous image.
                await using Stream stream = model.Image.OpenReadStream();
                await _imageStorage.SaveAsync(item.CatalogId, item.ItemId, stream);
                item.ImageContentType = model.Image.ContentType;
            }
            else if (model.RemoveImage && !string.IsNullOrEmpty(item.ImageContentType))
            {
                _imageStorage.Delete(item.CatalogId, item.ItemId);
                item.ImageContentType = null;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Item updated.";
            return RedirectToAction(nameof(Index));
        }

        // ===== DELETE ====================================================

        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);

            return item == null ? NotFound() : View(item);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string Itemid)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == Itemid && i.CatalogId == CurrentCatalogId);

            if (item != null)
            {
                if (!string.IsNullOrEmpty(item.ImageContentType))
                    _imageStorage.Delete(item.CatalogId, item.ItemId);

                _context.CatalogItems.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item deleted.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===== HELPERS ===================================================

        /*
         * Function: ValidateUploadedImage(IFormFile? file)
         * Description: Adds ModelState errors when the upload (if any)
         *              exceeds the size limit or is not an allowed image
         *              content type. Empty / null file is treated as
         *              "no upload" and is fine.
         * Parameter: IFormFile? file - the uploaded file from the form.
         * Return: void; populates ModelState in place.
         */
        private void ValidateUploadedImage(IFormFile? file)
        {
            if (file == null || file.Length == 0) return;

            if (file.Length > MaxImageBytes)
            {
                ModelState.AddModelError(nameof(CatalogItemFormViewModel.Image),
                    $"Image must be {MaxImageBytes / (1024 * 1024)} MB or smaller.");
                return;
            }

            if (!AllowedImageContentTypes.Contains(file.ContentType))
            {
                ModelState.AddModelError(nameof(CatalogItemFormViewModel.Image),
                    "Only JPEG, PNG, or WebP images are accepted.");
            }
        }
    }
}
