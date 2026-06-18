using Microsoft.AspNetCore.Identity.UI.Services;

namespace Autorep.Web.Services;

// Development / fallback implementation. Logs would-be emails to ILogger so
// the password reset flow is fully testable locally without configuring a
// real email provider. Registered when Graph:SendingMailbox is empty.
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // Log only the recipient + subject — NEVER the body. Password-reset / 2FA bodies embed
        // single-use token links, which must not be persisted to logs/App Insights.
        _logger.LogInformation("[EMAIL — not sent, logging only] To: {Email}  Subject: {Subject}", email, subject);
        return Task.CompletedTask;
    }
}
