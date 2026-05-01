/*
 * FILE: ErrorViewModel.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: View model used by the Error view to surface a request id for
 *              traceability when an unhandled exception occurs.
 */

namespace HomeVault.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}