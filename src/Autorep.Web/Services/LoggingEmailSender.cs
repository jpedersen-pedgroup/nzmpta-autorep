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
        _logger.LogInformation(
            "[EMAIL — not sent, logging only]\n  To:      {Email}\n  Subject: {Subject}\n  Body:\n{Body}",
            email, subject, htmlMessage);
        return Task.CompletedTask;
    }
}
