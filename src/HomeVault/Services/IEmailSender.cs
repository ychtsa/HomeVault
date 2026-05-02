/*
 * FILE: IEmailSender.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-01
 * DESCRIPTION: Abstraction over outgoing email so the controllers don't
 *              depend on any specific transport. Production deployments
 *              register an SMTP-backed implementation; development uses
 *              LogEmailSender, which writes the would-be email to the app
 *              log so a developer can copy the password-reset link without
 *              configuring a real mail server.
 */

namespace HomeVault.Services
{
    public interface IEmailSender
    {
        /*
         * Function: SendAsync(string to, string subject, string body)
         * Description: Delivers a single email message. Implementations may
         *              be synchronous (in which case Task.CompletedTask is
         *              returned) or genuinely asynchronous.
         * Parameter: string to - recipient email address.
         * Parameter: string subject - subject line.
         * Parameter: string body - plain-text body.
         * Return: Task - completes when the message has been handed off.
         */
        Task SendAsync(string to, string subject, string body);
    }
}
