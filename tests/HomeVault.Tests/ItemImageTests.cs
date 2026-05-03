/*
 * FILE: ItemImageTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Verifies the photo-upload behaviour on ItemsController:
 *              size + content-type validation, storage invocation on
 *              create / edit / remove, and image cleanup on item delete.
 */

using System.Security.Claims;
using HomeVault.Controllers;
using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using HomeVault.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HomeVault.Tests
{
    public class ItemImageTests
    {
        private const string CatalogId = "catA";
        private const string ResidentId = "rA";

        /*
         * Class: TestRig
         * Description: Builds an authenticated ItemsController bound to a
         *              fresh in-memory database with one existing item and
         *              mocked image storage / report generator.
         */
        private class TestRig
        {
            public CatalogDbContext Context { get; }
            public ItemsController Controller { get; }
            public Mock<ICatalogImageStorage> Storage { get; } = new();

            public TestRig()
            {
                DbContextOptions<CatalogDbContext> options =
                    new DbContextOptionsBuilder<CatalogDbContext>()
                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                        .Options;
                Context = new CatalogDbContext(options);
                Seed();

                IInsuranceReportGenerator reportGen = new Mock<IInsuranceReportGenerator>().Object;
                Controller = new ItemsController(Context, Storage.Object, reportGen);

                List<Claim> claims = new()
                {
                    new Claim(ClaimTypes.Name, "alice"),
                    new Claim("ResidentId", ResidentId),
                    new Claim("CatalogId", CatalogId)
                };
                ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "TestAuth"));
                DefaultHttpContext http = new() { User = principal };
                Controller.ControllerContext = new ControllerContext { HttpContext = http };
                Controller.TempData = new TempDataDictionary(http, new Mock<ITempDataProvider>().Object);
            }

            private void Seed()
            {
                Context.Catalogs.Add(new Catalog { CatalogId = CatalogId });
                Context.CatalogItems.Add(new CatalogItem
                {
                    ItemId = "i0001",
                    CatalogId = CatalogId,
                    ItemName = "Existing Laptop",
                    ItemType = "Electronics",
                    ItemValue = 1500,
                    ImageContentType = "image/jpeg"
                });
                Context.SaveChanges();
            }
        }

        /*
         * Function: BuildFormFile(long size, string contentType)
         * Description: Mocked IFormFile that reports the supplied size /
         *              type and exposes a 4-byte JPEG-magic stream when
         *              opened. Plenty for upload-validation tests.
         */
        private static IFormFile BuildFormFile(long size, string contentType)
        {
            Mock<IFormFile> mock = new();
            mock.Setup(f => f.Length).Returns(size);
            mock.Setup(f => f.ContentType).Returns(contentType);
            mock.Setup(f => f.OpenReadStream())
                .Returns(() => new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
            return mock.Object;
        }

        // ===== CREATE ====================================================

        [Fact]
        public async Task Create_WithoutImage_SucceedsWithNullContentType()
        {
            TestRig rig = new();
            CatalogItemFormViewModel model = new()
            {
                ItemName = "Standalone Phone",
                ItemType = "Electronics",
                ItemValue = 999,
                Image = null
            };

            IActionResult result = await rig.Controller.Create(model);

            Assert.IsType<RedirectToActionResult>(result);
            CatalogItem inserted = rig.Context.CatalogItems
                .Single(i => i.ItemName == "Standalone Phone");
            Assert.Null(inserted.ImageContentType);
            rig.Storage.Verify(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
        }

        [Fact]
        public async Task Create_WithValidImage_StoresFileAndSetsContentType()
        {
            TestRig rig = new();
            IFormFile file = BuildFormFile(size: 100_000, contentType: "image/jpeg");
            CatalogItemFormViewModel model = new()
            {
                ItemName = "Camera",
                ItemType = "Electronics",
                ItemValue = 800,
                Image = file
            };

            IActionResult result = await rig.Controller.Create(model);

            Assert.IsType<RedirectToActionResult>(result);
            CatalogItem inserted = rig.Context.CatalogItems
                .Single(i => i.ItemName == "Camera");
            Assert.Equal("image/jpeg", inserted.ImageContentType);
            rig.Storage.Verify(s => s.SaveAsync(
                CatalogId, inserted.ItemId, It.IsAny<Stream>()), Times.Once);
        }

        [Fact]
        public async Task Create_WithOversizedImage_AddsModelErrorAndDoesNotStore()
        {
            TestRig rig = new();
            IFormFile file = BuildFormFile(size: 6 * 1024 * 1024, contentType: "image/jpeg");
            CatalogItemFormViewModel model = new()
            {
                ItemName = "Big",
                ItemType = "Electronics",
                ItemValue = 10,
                Image = file
            };

            IActionResult result = await rig.Controller.Create(model);

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);
            Assert.Contains(rig.Controller.ModelState[nameof(model.Image)]!.Errors,
                e => e.ErrorMessage.Contains("MB"));
            rig.Storage.Verify(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
        }

        [Fact]
        public async Task Create_WithDisallowedContentType_AddsModelErrorAndDoesNotStore()
        {
            TestRig rig = new();
            IFormFile file = BuildFormFile(size: 100, contentType: "application/pdf");
            CatalogItemFormViewModel model = new()
            {
                ItemName = "Sneaky",
                ItemType = "Electronics",
                ItemValue = 10,
                Image = file
            };

            IActionResult result = await rig.Controller.Create(model);

            Assert.IsType<ViewResult>(result);
            Assert.False(rig.Controller.ModelState.IsValid);
            Assert.Contains(rig.Controller.ModelState[nameof(model.Image)]!.Errors,
                e => e.ErrorMessage.Contains("JPEG"));
            rig.Storage.Verify(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
        }

        // ===== EDIT ======================================================

        [Fact]
        public async Task Edit_WithRemoveImageFlag_ClearsContentTypeAndDeletesFile()
        {
            TestRig rig = new();
            CatalogItemFormViewModel model = new()
            {
                ItemId = "i0001",
                ItemName = "Existing Laptop",
                ItemType = "Electronics",
                ItemValue = 1500,
                RemoveImage = true
            };

            IActionResult result = await rig.Controller.Edit(model);

            Assert.IsType<RedirectToActionResult>(result);
            CatalogItem reloaded = rig.Context.CatalogItems.Single(i => i.ItemId == "i0001");
            Assert.Null(reloaded.ImageContentType);
            rig.Storage.Verify(s => s.Delete(CatalogId, "i0001"), Times.Once);
        }

        [Fact]
        public async Task Edit_WithNewImage_ReplacesExistingFile()
        {
            TestRig rig = new();
            IFormFile newFile = BuildFormFile(size: 5_000, contentType: "image/png");
            CatalogItemFormViewModel model = new()
            {
                ItemId = "i0001",
                ItemName = "Existing Laptop",
                ItemType = "Electronics",
                ItemValue = 1500,
                Image = newFile
            };

            IActionResult result = await rig.Controller.Edit(model);

            Assert.IsType<RedirectToActionResult>(result);
            CatalogItem reloaded = rig.Context.CatalogItems.Single(i => i.ItemId == "i0001");
            Assert.Equal("image/png", reloaded.ImageContentType);
            rig.Storage.Verify(s => s.SaveAsync(CatalogId, "i0001", It.IsAny<Stream>()), Times.Once);
        }

        // ===== DELETE ====================================================

        [Fact]
        public async Task DeleteConfirmed_RemovesAssociatedImageFile()
        {
            TestRig rig = new();

            IActionResult result = await rig.Controller.DeleteConfirmed("i0001");

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(rig.Context.CatalogItems.Where(i => i.ItemId == "i0001"));
            rig.Storage.Verify(s => s.Delete(CatalogId, "i0001"), Times.Once);
        }
    }
}
