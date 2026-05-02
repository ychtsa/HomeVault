/*
 * FILE: ResetPasswordViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-01
 * DESCRIPTION: View model for the password-reset form. The token arrives in
 *              the URL (delivered via email) and is round-tripped through a
 *              hidden field; the same password rules as signup apply.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.ViewModels
{
    public class ResetPasswordViewModel
    {
        [Required]
        public string Token { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 8,
            ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
            ErrorMessage = "Password must contain at least one letter and one digit.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = "";
    }
}
