/*
 * FILE: PdfReportTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Verifies QuestPdfInsuranceReportGenerator returns syntactically
 *              valid PDF bytes for both populated and empty catalogs.
 */

using HomeVault.Models.Entities;
using HomeVault.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace HomeVault.Tests
{
    public class PdfReportTests
    {
        // QuestPDF refuses to render until a license type is set. The web
        // host configures this in Program.cs; tests must do it themselves.
        static PdfReportTests()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private static Resident BuildResident() => new()
        {
            ResidentId = "r001",
            ResidentName = "Test Resident",
            ResidentAddress = "123 Test Street",
            CatalogId = "cat01"
        };

        /*
         * Function: AssertIsPdf(byte[] bytes)
         * Description: A valid PDF starts with the four-byte signature
         *              "%PDF" (0x25 0x50 0x44 0x46). Confirming this proves
         *              the generator produced an actual PDF rather than
         *              throwing or returning empty.
         */
        private static void AssertIsPdf(byte[] bytes)
        {
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 100, "PDF is implausibly small.");
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'D', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
        }

        [Fact]
        public void Generate_WithItems_ProducesValidPdf()
        {
            QuestPdfInsuranceReportGenerator gen = new();
            List<CatalogItem> items = new()
            {
                new CatalogItem { ItemId = "i01", CatalogId = "cat01",
                    ItemName = "MacBook Pro", ItemType = "Electronics", ItemValue = 2499 },
                new CatalogItem { ItemId = "i02", CatalogId = "cat01",
                    ItemName = "Diamond Ring", ItemType = "Jewelry", ItemValue = 3500 }
            };

            byte[] pdf = gen.Generate(BuildResident(), items);

            AssertIsPdf(pdf);
        }

        [Fact]
        public void Generate_WithEmptyCatalog_StillProducesValidPdf()
        {
            QuestPdfInsuranceReportGenerator gen = new();

            byte[] pdf = gen.Generate(BuildResident(), Array.Empty<CatalogItem>());

            AssertIsPdf(pdf);
        }
    }
}
