using Microsoft.AspNetCore.Identity.UI.Services;

namespace Autorep.Web.Tests;

// Records outgoing mail so tests can assert on notifications. Thread-safe because the
// factory is shared across a test class and tests may run against it concurrently.
public class CapturingEmailSender : IEmailSender
{
    public record Sent(string Email, string Subject, string HtmlMessage);

    private readonly List<Sent> _sent = new();
    public IReadOnlyList<Sent> All { get { lock (_sent) return _sent.ToList(); } }

    /// <summary>When set, sending to a matching recipient throws — stands in for a bad address
    /// or a throttled transport.</summary>
    public Func<string, bool>? FailFor { get; set; }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (FailFor?.Invoke(email) == true)
            throw new InvalidOperationException($"Simulated transport failure for {email}.");
        lock (_sent) _sent.Add(new Sent(email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}
