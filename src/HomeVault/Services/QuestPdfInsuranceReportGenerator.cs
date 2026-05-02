/*
 * FILE: QuestPdfInsuranceReportGenerator.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: QuestPDF-backed implementation of IInsuranceReportGenerator.
 *              Produces a one- or multi-page A4 PDF the resident can hand to
 *              an insurer: cover header with the resident's identity, an
 *              itemised inventory table, the running total of estimated
 *              value, and page numbers.
 */

using HomeVault.Models.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HomeVault.Services
{
    public class QuestPdfInsuranceReportGenerator : IInsuranceReportGenerator
    {
        public byte[] Generate(Resident resident, IReadOnlyList<CatalogItem> items)
        {
            int total = items.Sum(i => i.ItemValue);
            string generatedOn = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("HomeVault — Home Inventory Report")
                            .FontSize(18).Bold().FontColor(Colors.Grey.Darken3);
                        col.Item().PaddingTop(2).Text(text =>
                        {
                            text.Span("Prepared for ").FontColor(Colors.Grey.Darken1);
                            text.Span(resident.ResidentName).Bold();
                        });
                        col.Item().Text(resident.ResidentAddress).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(2).Text($"Generated {generatedOn}")
                            .FontSize(9).FontColor(Colors.Grey.Medium);
                        col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(12).Column(col =>
                    {
                        if (items.Count == 0)
                        {
                            col.Item().PaddingTop(40).AlignCenter()
                                .Text("This catalog is empty.")
                                .FontColor(Colors.Grey.Medium);
                            return;
                        }

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Item");
                                header.Cell().Element(HeaderCell).Text("Type");
                                header.Cell().Element(HeaderCell).AlignRight().Text("Estimated Value");
                            });

                            foreach (CatalogItem item in items)
                            {
                                table.Cell().Element(BodyCell).Text(item.ItemName);
                                table.Cell().Element(BodyCell).Text(item.ItemType);
                                table.Cell().Element(BodyCell).AlignRight().Text(item.GetFormattedValue());
                            }
                        });

                        col.Item().PaddingTop(12).AlignRight().Text(text =>
                        {
                            text.Span($"Total Estimated Value: ").Bold();
                            text.Span($"${total:N2}").Bold().FontSize(13);
                        });

                        col.Item().PaddingTop(20).Text(
                            "This report was produced from the resident's HomeVault catalog. " +
                            "Estimated values are entered by the resident and may not reflect " +
                            "current market or replacement value.")
                            .FontSize(9).FontColor(Colors.Grey.Medium);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Medium));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static IContainer HeaderCell(IContainer cell) =>
            cell.DefaultTextStyle(x => x.SemiBold())
                .PaddingVertical(6)
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Darken1);

        private static IContainer BodyCell(IContainer cell) =>
            cell.PaddingVertical(5)
                .BorderBottom(0.5f)
                .BorderColor(Colors.Grey.Lighten2);
    }
}
