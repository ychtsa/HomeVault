/*
 * FILE: Resident.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Entity for the person who owns a residence and a catalog.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Entities
{
    public class Resident
    {
        [Key]
        [StringLength(5)]
        public string ResidentId { get; set; } = null!;

        [Required, StringLength(30)]
        public string ResidentName { get; set; } = null!;

        [Required, StringLength(50)]
        public string ResidentAddress { get; set; } = null!;

        [Required, StringLength(5)]
        public string CatalogId { get; set; } = null!;

        // Navigation properties
        public Catalog Catalog { get; set; } = null!;
        public ResidentUser? User { get; set; }
    }
}