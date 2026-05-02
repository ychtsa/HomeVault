/*
 * FILE: CatalogItemFormViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: View model for the create / edit catalog item forms.
 *              Carries optional photo upload and (for Edit) a flag the user
 *              can toggle to remove the existing photo without uploading
 *              a replacement.
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

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

        [Display(Name = "Photo")]
        public IFormFile? Image { get; set; }

        // Edit form only: lets the user clear the existing photo when
        // submitting without uploading a replacement.
        [Display(Name = "Remove existing photo")]
        public bool RemoveImage { get; set; }

        // Edit form only: pre-populated by the controller so the view can
        // render a "currently uploaded" thumbnail.
        public bool HasExistingImage { get; set; }
    }
}
