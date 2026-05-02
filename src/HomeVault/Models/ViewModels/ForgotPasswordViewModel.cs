/*
 * FILE: ForgotPasswordViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-01
 * DESCRIPTION: View model for the "forgot password" form. Captures only the
 *              email address; the controller looks up the corresponding user
 *              and (if any) issues a single-use reset token.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required, StringLength(100), EmailAddress]
        public string Email { get; set; } = "";
    }
}
