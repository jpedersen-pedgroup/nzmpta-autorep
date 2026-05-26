using Azure.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Autorep.Web.Services;

// Sends email via Microsoft Graph using the configured M365 mailbox. Auth is
// via DefaultAzureCredential: locally falls back to your Azure CLI / VS
// identity; in Azure runs as the App Service's System-Assigned Managed
// Identity. The MI must have the application permission Mail.Send granted,
// ideally scoped via an Application Access Policy to only the SendingMailbox.
public class GraphEmailSender : IEmailSender
{
    private readonly GraphServiceClient _graph;
    private readonly string _sendingMailbox;
    private readonly ILogger<GraphEmailSender> _logger;

    public GraphEmailSender(IConfiguration config, ILogger<GraphEmailSender> logger)
    {
        _sendingMailbox = config["Graph:SendingMailbox"]
            ?? throw new InvalidOperationException("Graph:SendingMailbox is not configured.");
        var credential = new DefaultAzureCredential();
        _graph = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var body = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlMessage
                },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = email } }
                }
            },
            SaveToSentItems = false
        };

        try
        {
            await _graph.Users[_sendingMailbox].SendMail.PostAsync(body);
            _logger.LogInformation(
                "Email sent via Graph: to={Email} subject={Subject}", email, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send email via Graph: to={Email} subject={Subject}", email, subject);
            throw;
        }
    }
}
