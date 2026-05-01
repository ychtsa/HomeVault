/*
 * FILE: CatalogItemFormViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: View model for the create / edit catalog item forms.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.ViewModels
{
    public class CatalogItemFormViewModel
    {
        public string? ItemId { get; set; }

        [Required, StringLength(30)]
        [Display(Name = "Item Name")]
        public string ItemName { get; set; } = "";

        [Required, StringLength(30)]
        [Display(Name = "Item Type")]
        public string ItemType { get; set; } = "";

        [Required, Range(1, int.MaxValue,
            ErrorMessage = "Value must be greater than 0.")]
        [Display(Name = "Estimated Value ($)")]
        public int ItemValue { get; set; }
    }
}