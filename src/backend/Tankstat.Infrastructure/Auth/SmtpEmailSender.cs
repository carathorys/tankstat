using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Tankstat.Application.Auth;

namespace Tankstat.Infrastructure.Auth;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    /// <summary>
    /// How long a whole send may take. Well within the GraphQL request's own 30 s, so a mail server that takes the connection but never
    /// answers is a failed e-mail (and an administrator still gets the link) instead of a request that times out with nothing.
    /// </summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    /// <summary><see cref="SendTimeout"/>; shorter in tests.</summary>
    internal TimeSpan Timeout { get; init; } = SendTimeout;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new InvalidOperationException("Smtp is not configured.");

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(o.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            var security = Enum.TryParse<SecureSocketOptions>(o.Security, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.Auto;
            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            await client.ConnectAsync(o.Host, o.Port, security, limit.Token);
            if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password, limit.Token);
            await client.SendAsync(message, limit.Token);
            await client.DisconnectAsync(true, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own limit, not the caller's cancellation (that one stays a cancellation).
            throw new EmailSendException($"Sending an e-mail through {o.Host}:{o.Port} failed (no answer within {Timeout.TotalSeconds:0.#} s)");
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
