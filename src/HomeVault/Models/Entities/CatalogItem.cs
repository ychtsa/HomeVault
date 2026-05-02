/*
 * FILE: CatalogItem.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Entity for a single insurable item within a resident's
 *              catalog. The image (if any) lives outside wwwroot under
 *              App_Data/uploads/{CatalogId}/{ItemId} and is served via the
 *              authorized Items/Image action — never via a static URL —
 *              so cross-tenant access requires beating the same CatalogId
 *              claim check that protects every other operation.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Entities
{
    public class CatalogItem
    {
        [Key]
        [StringLength(5)]
        public string ItemId { get; set; } = null!;

        [Required, StringLength(5)]
        public string CatalogId { get; set; } = null!;

        [Required, StringLength(30)]
        public string ItemName { get; set; } = null!;

        [Required, StringLength(30)]
        public string ItemType { get; set; } = null!;

        public int ItemValue { get; set; }

        // Null when no photo has been uploaded; otherwise the IANA media
        // type of the stored image (e.g. "image/jpeg") so the controller
        // can return the correct Content-Type without sniffing the file.
        [StringLength(60)]
        public string? ImageContentType { get; set; }

        // Navigation property
        public Catalog Catalog { get; set; } = null!;

        /*
         * Function: GetFormattedValue()
         * Description: Returns the item value formatted as a US-style dollar
         *              string with thousands separators and two decimals.
         * Parameter: none.
         * Return: string formattedValue - e.g. "$1,250.00".
         */
        public string GetFormattedValue() => "$" + ItemValue.ToString("N2");
    }
}