using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Infrastructure.Auth;

namespace Tankstat.Infrastructure.UnitTests;

public class SmtpEmailSenderTests
{
    /// <summary>
    /// A mail server that turns every recipient away, quoting the address in its answer, as real servers do; or, with
    /// <c>acceptButIgnoreGoodbye</c>, one that takes the message and then never answers QUIT.
    /// </summary>
    private sealed class TestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly bool _accept;
        private readonly Task _serving;

        public TestServer(bool acceptButIgnoreGoodbye = false)
        {
            _accept = acceptButIgnoreGoodbye;
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Whether a whole message came in.</summary>
        public bool Received { get; private set; }

        /// <summary>Completes when the client said QUIT.</summary>
        public TaskCompletionSource Goodbye { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase) && !_accept) await writer.WriteLineAsync($"550 5.1.1 {line["RCPT TO:".Length..].Trim()}: Recipient address rejected: User unknown");
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 Go ahead");
                        while (await reader.ReadLineAsync() is { } body && body != ".") { }
                        Received = true;
                        await writer.WriteLineAsync("250 Queued");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        Goodbye.TrySetResult();
                        if (_accept) continue; // says nothing; the client gives up and closes
                        await writer.WriteLineAsync("221 Bye");
                        break;
                    }
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

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static SmtpEmailSender Sender(int port) =>
        new(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = port, From = "tank@example.com", Security = "None" }));

    [Fact]
    public async Task AMailServerThatRejectsTheRecipient_DoesNotPutTheAddressInTheException()
    {
        using var server = new TestServer();

        var error = await Assert.ThrowsAsync<EmailSendException>(() => Sender(server.Port).SendAsync("canary.person@canary-mail.example", "Subject", "Body", default));

        Assert.DoesNotContain("canary", error.ToString(), StringComparison.OrdinalIgnoreCase); // not in the message, and no inner exception that holds it
        Assert.Contains($"127.0.0.1:{server.Port}", error.Message);
        Assert.Contains("550", error.Message); // what the server said, without how it worded it: its answer quotes the address
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task AMessageTheServerTook_IsSent_EvenWhenTheSendIsCancelledDuringTheGoodbye()
    {
        // The app stopping while the server keeps the goodbye waiting: the message is out, so the link it carries must not be taken back.
        // MailKit's DisconnectAsync swallows what goes wrong with QUIT, a cancellation included; this keeps an upgrade from changing that.
        using var server = new TestServer(acceptButIgnoreGoodbye: true);
        using var stopping = new CancellationTokenSource();

        var send = Sender(server.Port).SendAsync("someone@example.com", "Subject", "Body", stopping.Token);
        await server.Goodbye.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await stopping.CancelAsync();

        await send.WaitAsync(TimeSpan.FromSeconds(10)); // neither a cancellation nor an EmailSendException
        Assert.True(server.Received);
    }

    [Fact]
    public async Task AMailServerThatCannotBeReached_IsAnEmailSendException_NamingTheServer()
    {
        var port = FreePort(); // nothing listens on it

        var error = await Assert.ThrowsAsync<EmailSendException>(() => Sender(port).SendAsync("someone@example.com", "Subject", "Body", default));

        Assert.Contains($"127.0.0.1:{port}", error.Message);
    }

    [Fact]
    public async Task AMailServerThatNeverAnswers_IsAnEmailSendException_WithinTheTimeout()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0); // takes the connection, never says a word
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = port, From = "tank@example.com", Security = "None" }))
        {
            Timeout = TimeSpan.FromMilliseconds(300),
        };

        var started = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<EmailSendException>(() => sender.SendAsync("canary.person@canary-mail.example", "Subject", "Body", default));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Contains($"127.0.0.1:{port}", error.Message);
        Assert.DoesNotContain("canary", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(SmtpEmailSender.SendTimeout < TimeSpan.FromSeconds(30)); // within the GraphQL request's own limit
    }

    [Fact]
    public async Task CancellingASend_IsStillACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(9).SendAsync("someone@example.com", "Subject", "Body", cancelled.Token));
    }
}
