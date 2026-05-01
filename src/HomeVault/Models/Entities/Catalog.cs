/*
 * FILE: Catalog.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Entity that represents a single resident's catalog (the
 *              container of all CatalogItems they own).
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Entities
{
    public class Catalog
    {
        [Key]
        [StringLength(5)]
        public string CatalogId { get; set; } = null!;

        // Navigation properties
        public Resident? Resident { get; set; }
        public ICollection<CatalogItem> Items { get; set; } = new List<CatalogItem>();
    }
}