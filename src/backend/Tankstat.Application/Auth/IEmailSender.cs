namespace Tankstat.Application.Auth;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>
/// An e-mail could not be sent. A mail server's answer often quotes the recipient's address, and an unexpected error ends up in the log,
/// so the message says what failed without that address: it is safe to log.
/// </summary>
public sealed class EmailSendException(string message) : Exception(message);
