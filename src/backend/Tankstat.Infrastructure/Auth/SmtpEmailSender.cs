using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Tankstat.Application.Auth;

namespace Tankstat.Infrastructure.Auth;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new InvalidOperationException("Smtp is not configured.");

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(o.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            var security = Enum.TryParse<SecureSocketOptions>(o.Security, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.Auto;
            using var client = new SmtpClient();
            await client.ConnectAsync(o.Host, o.Port, security, ct);
            if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The server's answer (and MailKit's own message) often quotes the recipient: leave the address, and the exception that holds it, out.
            throw new EmailSendException($"Sending an e-mail through {o.Host}:{o.Port} failed ({e.GetType().Name}: {e.Message.Replace(to, "<recipient>", StringComparison.OrdinalIgnoreCase)})");
        }
    }
}
