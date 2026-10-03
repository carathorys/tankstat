using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Infrastructure.Auth;

namespace Tankstat.Infrastructure.UnitTests;

public class SmtpEmailSenderTests
{
    /// <summary>A mail server that turns every recipient away, quoting the address in its answer, as real servers do.</summary>
    private sealed class RejectingServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;

        public RejectingServer()
        {
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 test ESMTP");
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 test");
                    else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync($"550 5.1.1 {line["RCPT TO:".Length..].Trim()}: Recipient address rejected: User unknown");
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 Bye"); break; }
                    else await writer.WriteLineAsync("250 Ok");
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                // the test is over and the listener is closed
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _serving.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private static SmtpEmailSender Sender(int port) =>
        new(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = port, From = "tank@example.com", Security = "None" }));

    [Fact]
    public async Task AMailServerThatRejectsTheRecipient_DoesNotPutTheAddressInTheException()
    {
        using var server = new RejectingServer();

        var error = await Assert.ThrowsAsync<EmailSendException>(() => Sender(server.Port).SendAsync("canary.person@canary-mail.example", "Subject", "Body", default));

        Assert.DoesNotContain("canary", error.ToString(), StringComparison.OrdinalIgnoreCase); // not in the message, and no inner exception that holds it
        Assert.Contains($"127.0.0.1:{server.Port}", error.Message);
        Assert.Contains("<recipient>", error.Message); // the server did quote the address: the guard replaced it
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task AMailServerThatCannotBeReached_IsAnEmailSendException_NamingTheServer()
    {
        var port = ((IPEndPoint)new Func<EndPoint>(() => { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var e = l.LocalEndpoint; l.Stop(); return e; })()).Port; // a port nothing listens on

        var error = await Assert.ThrowsAsync<EmailSendException>(() => Sender(port).SendAsync("someone@example.com", "Subject", "Body", default));

        Assert.Contains($"127.0.0.1:{port}", error.Message);
    }

    [Fact]
    public async Task CancellingASend_IsStillACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(9).SendAsync("someone@example.com", "Subject", "Body", cancelled.Token));
    }
}
