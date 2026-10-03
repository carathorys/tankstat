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
            // What a server answers to a command (a recipient it refuses) usually quotes the address, in any form it likes, so only its
            // status is kept. The other failures (connecting, signing in, TLS) have nothing to do with the recipient; its address is still
            // blanked out of their text, and the exception itself is left out, since it holds the same message.
            var why = e is SmtpCommandException command 
                ? $"the server answered {(int)command.StatusCode} ({command.ErrorCode})"
                : $"{e.GetType().Name}: {e.Message.Replace(to, "<recipient>", StringComparison.OrdinalIgnoreCase)}";
            throw new EmailSendException($"Sending an e-mail through {o.Host}:{o.Port} failed ({why})");
        }
    }
}
