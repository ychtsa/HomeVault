/*
 * FILE: ItemsController.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: CRUD controller for catalog items, scoped at the database
 *              level by the CatalogId stored in the signed-in user's
 *              cookie claims. Every read and write is filtered by that
 *              claim, which prevents one user from seeing or modifying
 *              another user's items even by guessing IDs.
 */

using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Controllers
{
    [Authorize]
    public class ItemsController : Controller
    {
        private readonly CatalogDbContext _context;

        /*
         * Function: ItemsController(CatalogDbContext context)
         * Description: Constructor. Captures the EF context via DI.
         * Parameter: CatalogDbContext context - the EF Core context.
         * Return: none (constructor).
         */
        public ItemsController(CatalogDbContext context)
        {
            _context = context;
        }

        /*
         * Property: CurrentCatalogId
         * Description: Reads the current user's CatalogId from claims so
         *              that every query in this controller can scope to it.
         * Return: string - the CatalogId, or "" if no claim is present.
         */
        private string CurrentCatalogId
        {
            get
            {
                string id = User.FindFirst("CatalogId")?.Value ?? "";
                return id;
            }
        }

        /*
         * Function: Index() [GET]
         * Description: Lists every CatalogItem belonging to the user's catalog.
         * Parameter: none.
         * Return: IActionResult result - the Index view bound to the items.
         */
        public async Task<IActionResult> Index()
        {
            List<CatalogItem> items = await _context.CatalogItems
                .Where(i => i.CatalogId == CurrentCatalogId)
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            IActionResult result = View(items);
            return result;
        }

        /*
         * Function: Create() [GET]
         * Description: Renders the empty Create form.
         * Parameter: none.
         * Return: IActionResult result - the Create view.
         */
        [HttpGet]
        public IActionResult Create()
        {
            IActionResult result = View(new CatalogItemFormViewModel());
            return result;
        }

        /*
         * Function: Create(CatalogItemFormViewModel model) [POST]
         * Description: Generates a 5-character ItemId and inserts the item
         *              under the current user's CatalogId.
         * Parameter: CatalogItemFormViewModel model - submitted form data.
         * Return: IActionResult result - redirect to Index on success, or
         *         redisplay Create with validation errors on failure.
         */
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CatalogItemFormViewModel model)
        {
            IActionResult result = View(model);

            if (ModelState.IsValid)
            {
                CatalogItem item = new CatalogItem
                {
                    ItemId = Guid.NewGuid().ToString("N").Substring(0, 5),
                    CatalogId = CurrentCatalogId,
                    ItemName = model.ItemName,
                    ItemType = model.ItemType,
                    ItemValue = model.ItemValue
                };

                _context.CatalogItems.Add(item);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"\"{item.ItemName}\" was added.";
                result = RedirectToAction(nameof(Index));
            }

            return result;
        }

        /*
         * Function: Edit(string id) [GET]
         * Description: Loads the item by id (scoped by CatalogId) and shows
         *              the Edit form pre-populated with current values.
         * Parameter: string id - the ItemId to edit.
         * Return: IActionResult result - the Edit view, or NotFound if the
         *         id does not belong to this user.
         */
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);

            IActionResult result;
            if (item == null)
            {
                result = NotFound();
            }
            else
            {
                CatalogItemFormViewModel model = new CatalogItemFormViewModel
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    ItemType = item.ItemType,
                    ItemValue = item.ItemValue
                };
                result = View(model);
            }

            return result;
        }

        /*
         * Function: Edit(CatalogItemFormViewModel model) [POST]
         * Description: Updates the row, scoped by CatalogId for authorization.
         * Parameter: CatalogItemFormViewModel model - submitted form data.
         * Return: IActionResult result - redirect to Index on success, or
         *         redisplay Edit with errors / NotFound on failure.
         */
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CatalogItemFormViewModel model)
        {
            IActionResult result = View(model);

            if (ModelState.IsValid && model.ItemId != null)
            {
                CatalogItem? item = await _context.CatalogItems
                    .FirstOrDefaultAsync(i => i.ItemId == model.ItemId
                                              && i.CatalogId == CurrentCatalogId);

                if (item == null)
                {
                    result = NotFound();
                }
                else
                {
                    item.ItemName = model.ItemName;
                    item.ItemType = model.ItemType;
                    item.ItemValue = model.ItemValue;
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Item updated.";
                    result = RedirectToAction(nameof(Index));
                }
            }

            return result;
        }

        /*
         * Function: Delete(string id) [GET]
         * Description: Renders the delete confirmation page.
         * Parameter: string id - the ItemId to confirm deletion of.
         * Return: IActionResult result - the Delete view, or NotFound.
         */
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);

            IActionResult result = item == null ? NotFound() : View(item);
            return result;
        }

        /*
         * Function: DeleteConfirmed(string Itemid) [POST]
         * Description: Performs the deletion. The query is scoped by
         *              CatalogId so a crafted request with another user's
         *              ItemId is silently ignored.
         * Parameter: string Itemid - the ItemId to delete.
         * Return: IActionResult result - redirect to Index.
         */
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string Itemid)
        {
            CatalogItem? item = await _context.CatalogItems
                .FirstOrDefaultAsync(i => i.ItemId == Itemid && i.CatalogId == CurrentCatalogId);

            if (item != null)
            {
                _context.CatalogItems.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Item deleted.";
            }

            IActionResult result = RedirectToAction(nameof(Index));
            return result;
        }
    }
}