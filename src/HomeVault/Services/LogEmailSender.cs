/*
 * FILE: LogEmailSender.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-01
 * DESCRIPTION: Development-friendly IEmailSender implementation that writes
 *              every outgoing email to the application log instead of
 *              transmitting it. Lets the password-reset flow run end-to-end
 *              without an SMTP server; a developer copies the reset link
 *              from the console / log file and pastes it into the browser.
 *              Replace this binding with an SMTP / SendGrid implementation
 *              before going to production.
 */

namespace HomeVault.Services
{
    public class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;

        public LogEmailSender(ILogger<LogEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string to, string subject, string body)
        {
            _logger.LogInformation(
                "[EMAIL] To={Recipient} | Subject={Subject}\n{Body}",
                to, subject, body);
            return Task.CompletedTask;
        }
    }
}
