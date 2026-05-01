/*
 * FILE: SignupViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: View model for the user registration form, with built-in
 *              validation rules (length, password match).
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.ViewModels
{
    public class SignupViewModel
    {
        [Required, StringLength(30)]
        [Display(Name = "Full Name")]
        public string ResidentName { get; set; } = "";

        [Required, StringLength(50)]
        [Display(Name = "Address")]
        public string ResidentAddress { get; set; } = "";

        [Required, StringLength(30, MinimumLength = 3)]
        public string Username { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 6,
            ErrorMessage = "Password must be at least 6 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = "";
    }
}