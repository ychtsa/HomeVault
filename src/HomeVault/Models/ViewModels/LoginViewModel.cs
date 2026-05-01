/*
 * FILE: LoginViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: View model for the login form. Carries the user's submitted
 *              credentials and the post-login return URL.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public string? ReturnUrl { get; set; }
    }
}