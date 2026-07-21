using Microsoft.AspNetCore.Identity.UI.Services;

namespace Autorep.Web.Tests;

// Records outgoing mail so tests can assert on notifications. Thread-safe because the
// factory is shared across a test class and tests may run against it concurrently.
public class CapturingEmailSender : IEmailSender
{
    public record Sent(string Email, string Subject, string HtmlMessage);

    private readonly List<Sent> _sent = new();
    public IReadOnlyList<Sent> All { get { lock (_sent) return _sent.ToList(); } }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        lock (_sent) _sent.Add(new Sent(email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}
