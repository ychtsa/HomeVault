/*
 * FILE: IInsuranceReportGenerator.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Abstraction over PDF generation. Hides the PDF library
 *              behind an interface so the controller can stay simple and
 *              the implementation can be swapped (e.g., for unit tests).
 */

using HomeVault.Models.Entities;

namespace HomeVault.Services
{
    public interface IInsuranceReportGenerator
    {
        /*
         * Function: Generate(Resident resident, IReadOnlyList<CatalogItem> items)
         * Description: Renders a printable inventory report covering the
         *              supplied items, attributed to the supplied resident.
         * Parameter: Resident resident - account holder details for the
         *            report header (name, address).
         * Parameter: IReadOnlyList<CatalogItem> items - all items to list.
         * Return: byte[] - the in-memory PDF document.
         */
        byte[] Generate(Resident resident, IReadOnlyList<CatalogItem> items);
    }
}
