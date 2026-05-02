/*
 * FILE: CatalogIsolationTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Proves the security claim that ItemsController.Index returns
 *              ONLY the items belonging to the signed-in user's CatalogId,
 *              even when the database contains items owned by other users.
 *              This is the most important security test in the project.
 */

using System.Security.Claims;
using HomeVault.Controllers;
using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HomeVault.Tests
{
    public class CatalogIsolationTests
    {
        /*
         * Function: BuildContext()
         * Description: Creates a fresh in-memory CatalogDbContext seeded
         *              with two users' catalogs and items.
         * Parameter: none.
         * Return: CatalogDbContext - a context isolated to this test only.
         */
        private static CatalogDbContext BuildContext()
        {
            DbContextOptions<CatalogDbContext> options =
                new DbContextOptionsBuilder<CatalogDbContext>()
                    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                    .Options;

            CatalogDbContext context = new CatalogDbContext(options);

            // Catalog A — owned by Alice
            context.Catalogs.Add(new Catalog { CatalogId = "catA" });
            context.CatalogItems.AddRange(
                new CatalogItem { ItemId = "a001", CatalogId = "catA",
                    ItemName = "Alice's Laptop", ItemType = "Electronics", ItemValue = 2000 },
                new CatalogItem { ItemId = "a002", CatalogId = "catA",
                    ItemName = "Alice's Couch",  ItemType = "Furniture",   ItemValue = 800 });

            // Catalog B — owned by Bob
            context.Catalogs.Add(new Catalog { CatalogId = "catB" });
            context.CatalogItems.AddRange(
                new CatalogItem { ItemId = "b001", CatalogId = "catB",
                    ItemName = "Bob's Phone", ItemType = "Electronics", ItemValue = 1000 });

            context.SaveChanges();
            return context;
        }

        /*
         * Function: BuildControllerForCatalog(CatalogDbContext, string)
         * Description: Wires up an ItemsController whose User claims contain
         *              the supplied CatalogId, simulating an authenticated
         *              session for that catalog.
         * Parameter: CatalogDbContext context - the test EF context.
         * Parameter: string catalogId - the CatalogId claim to inject.
         * Return: ItemsController - configured controller ready to test.
         */
        private static ItemsController BuildControllerForCatalog(
            CatalogDbContext context, string catalogId)
        {
            ICatalogImageStorage storage = new Mock<ICatalogImageStorage>().Object;
            ItemsController controller = new ItemsController(context, storage);

            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "tester"),
                new Claim("ResidentId", "rest1"),
                new Claim("CatalogId", catalogId)
            };
            ClaimsIdentity identity = new ClaimsIdentity(claims, "TestAuth");
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            return controller;
        }

        /*
         * Function: Index_ReturnsOnlyOwnCatalogItems()
         * Description: Logs in as Alice (CatalogId = "catA") and asserts
         *              that Index returns only Alice's two items, never
         *              Bob's item, even though Bob's item lives in the
         *              same database.
         * Parameter: none.
         * Return: Task (async test).
         */
        [Fact]
        public async Task Index_ReturnsOnlyOwnCatalogItems()
        {
            CatalogDbContext context = BuildContext();
            ItemsController controller = BuildControllerForCatalog(context, "catA");

            IActionResult actionResult = await controller.Index();

            ViewResult viewResult = Assert.IsType<ViewResult>(actionResult);
            List<CatalogItem> items = Assert.IsType<List<CatalogItem>>(viewResult.Model);

            Assert.Equal(2, items.Count);
            Assert.All(items, i => Assert.Equal("catA", i.CatalogId));
            Assert.DoesNotContain(items, i => i.CatalogId == "catB");
        }

        /*
         * Function: Edit_ReturnsNotFound_WhenAccessingAnotherUsersItem()
         * Description: Logs in as Alice and tries to GET the Edit page for
         *              Bob's item id. The controller must return NotFound,
         *              proving that ItemId-guessing cannot leak data across
         *              catalogs.
         * Parameter: none.
         * Return: Task (async test).
         */
        [Fact]
        public async Task Edit_ReturnsNotFound_WhenAccessingAnotherUsersItem()
        {
            CatalogDbContext context = BuildContext();
            ItemsController controller = BuildControllerForCatalog(context, "catA");

            IActionResult actionResult = await controller.Edit("b001");

            Assert.IsType<NotFoundResult>(actionResult);
        }

        /*
         * Function: Delete_ReturnsNotFound_WhenAccessingAnotherUsersItem()
         * Description: Logs in as Alice and tries to GET the Delete
         *              confirmation page for Bob's item id. Must return
         *              NotFound for the same isolation reason.
         * Parameter: none.
         * Return: Task (async test).
         */
        [Fact]
        public async Task Delete_ReturnsNotFound_WhenAccessingAnotherUsersItem()
        {
            CatalogDbContext context = BuildContext();
            ItemsController controller = BuildControllerForCatalog(context, "catA");

            IActionResult actionResult = await controller.Delete("b001");

            Assert.IsType<NotFoundResult>(actionResult);
        }
    }
}