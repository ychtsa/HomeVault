/*
 * FILE: CatalogItem.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Entity for a single insurable item within a resident's catalog.
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