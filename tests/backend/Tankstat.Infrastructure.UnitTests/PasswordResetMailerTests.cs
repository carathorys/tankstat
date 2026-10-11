using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;
using Tankstat.Infrastructure.Auth;

namespace Tankstat.Infrastructure.UnitTests;

public class PasswordResetMailerTests
{
    /// <summary>
    /// Sends to a list; a script for an address decides what a send to it does instead (throw, or wait until the send is cancelled). By
    /// address, not by turn: the mailer serves several requests at once, in no fixed order.
    /// </summary>
    private sealed class ScriptedEmail : IEmailSender
    {
        private readonly SemaphoreSlim _starts = new(0);

        public List<string> To { get; } = [];
        public Dictionary<string, Func<CancellationToken, Task>> Scripts { get; } = [];
        public bool IsConfigured => true;

        public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            _starts.Release();
            if (Scripts.TryGetValue(to, out var script)) await script(ct);
            lock (To) To.Add(to);
        }

        /// <summary>Waits until this many sends have started (in all, counting from the first).</summary>
        public async Task StartedAsync(int count = 1)
        {
            for (var i = 0; i < count; i++) Assert.True(await _starts.WaitAsync(TimeSpan.FromSeconds(30)));
        }

        public string[] Sent()
        {
            lock (To) return [.. To];
        }
    }

    private static Task Hang(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct); // a mail server that never answers

    private static TestDatabase Database(ScriptedEmail email) => new(
        new() { ["Auth:Mode"] = "Standalone", ["Auth:PublicUrl"] = "https://tank.test", ["Auth:Standalone:ResetCooldownMinutes"] = "0" },
        s => s.AddSingleton<IEmailSender>(email));

    private static async Task<User> AddUser(TestDatabase db, string email)
    {
        var user = User.CreateLocal(email, null, false);
        await db.Get<IUserRepository>().AddAsync(user, default);
        return user;
    }

    private static PasswordResetMailer Mailer(TestDatabase db) =>
        new(db.Get<IServiceScopeFactory>(), db.Get<PasswordResetQueue>(), db.Get<ILogger<PasswordResetMailer>>());

    [Fact]
    public void TheQueue_HoldsAUserOnce_AndTakesNothingWhenFull()
    {
        var queue = new PasswordResetQueue(capacity: 2);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(ResetRequestQueueing.Queued, queue.Enqueue(a, 0));
        Assert.Equal(ResetRequestQueueing.AlreadyWaiting, queue.Enqueue(a, 0));
        Assert.Equal(ResetRequestQueueing.Queued, queue.Enqueue(b, 0));
        Assert.Equal(ResetRequestQueueing.QueueFull, queue.Enqueue(c, 0));
        Assert.False(queue.WhenIdleAsync().IsCompleted);
    }

    [Fact]
    public async Task AUserAskingAgainWhileWaiting_IsServedWithTheSessionVersionOfTheLaterRequest()
    {
        var queue = new PasswordResetQueue(capacity: 10);
        var a = Guid.NewGuid();

        queue.Enqueue(a, 1);
        Assert.Equal(ResetRequestQueueing.AlreadyWaiting, queue.Enqueue(a, 2)); // asked again after a new password

        Assert.Equal((a, 2), await queue.TakeAsync(default));
    }

    [Fact]
    public async Task ATakenRequest_LetsTheSameUserBeQueuedAgain_AndTheQueueIsIdleOnceAllAreDone()
    {
        var queue = new PasswordResetQueue(capacity: 10);
        var a = Guid.NewGuid();
        Assert.True(queue.WhenIdleAsync().IsCompleted);
        queue.Enqueue(a, 0);

        Assert.Equal(a, (await queue.TakeAsync(default)).UserId);
        Assert.Equal(ResetRequestQueueing.Queued, queue.Enqueue(a, 0)); // the waiting one was taken: the cool-down decides from here
        Assert.Equal(a, (await queue.TakeAsync(default)).UserId);
        queue.Done();
        Assert.False(queue.WhenIdleAsync().IsCompleted);
        queue.Done();
        Assert.True(queue.WhenIdleAsync().IsCompleted);
    }

    [Fact]
    public async Task AQueuedRequest_IsSentInAScopeOfItsOwn()
    {
        var email = new ScriptedEmail();
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await db.Get<PasswordResetQueue>().WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(["alice@example.com"], email.To);
        Assert.Single(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default));
        await mailer.StopAsync(default);
    }

    [Fact]
    public async Task AFailingRequest_IsLogged_AndTheNextOneIsStillServed()
    {
        var email = new ScriptedEmail { Scripts = { ["alice@example.com"] = _ => throw new InvalidOperationException("the sender is broken") } };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        var bob = await AddUser(db, "bob@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        db.Get<IPasswordResetQueue>().Enqueue(bob.Id, bob.SessionVersion);
        await db.Get<PasswordResetQueue>().WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(["bob@example.com"], email.To);
        var error = Assert.Single(db.Log.From<PasswordResetMailer>(), e => e.Level == LogLevel.Error);
        Assert.Equal(alice.Id, error.Values["UserId"]);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Empty(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default)); // her link went back
        await mailer.StopAsync(default);
    }

    [Fact]
    public async Task AMailServerFailure_IsAWarning_NotAnError()
    {
        var email = new ScriptedEmail { Scripts = { ["alice@example.com"] = _ => throw new EmailSendException("Sending an e-mail through smtp.test:25 failed (refused)") } };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await db.Get<PasswordResetQueue>().WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(db.Log.Entries, e => e.Level == LogLevel.Error);
        var warning = Assert.Single(db.Log.From<PasswordResetService>(), e => e.Level == LogLevel.Warning);
        Assert.Equal(alice.Id, warning.Values["UserId"]);
        Assert.False(db.Log.Mentions("alice@example.com"));
        await mailer.StopAsync(default);
    }

    [Fact]
    public async Task StoppingDuringASend_LeavesNoLinkThatNobodyReceived()
    {
        var email = new ScriptedEmail { Scripts = { ["alice@example.com"] = Hang } };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await email.StartedAsync();
        await mailer.StopAsync(default);

        Assert.Empty(email.To);
        Assert.Empty(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default));
        Assert.DoesNotContain(db.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(db.Log.From<PasswordResetMailer>(), e => e.Level == LogLevel.Information && Equals(e.Values.GetValueOrDefault("UserId"), alice.Id));
    }

    [Fact]
    public async Task AStopThatTheSenderReportsAsAnotherFailure_IsNoError()
    {
        // Not every provider reports a cancelled call as an OperationCanceledException (SqlClient has its own exception for it).
        var email = new ScriptedEmail
        {
            Scripts =
            {
                ["alice@example.com"] = async ct =>
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { throw new InvalidOperationException("the connection was closed"); }
                },
            },
        };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await email.StartedAsync();
        await mailer.StopAsync(default);

        Assert.DoesNotContain(db.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Empty(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default));
        Assert.Contains(db.Log.From<PasswordResetMailer>(), e => e.Level == LogLevel.Information && Equals(e.Values.GetValueOrDefault("UserId"), alice.Id));
    }

    [Fact]
    public async Task AMailServerThatDoesNotAnswerOneSend_HoldsNoOtherUsersMailBack()
    {
        var email = new ScriptedEmail { Scripts = { ["alice@example.com"] = Hang } };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        var bob = await AddUser(db, "bob@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await email.StartedAsync();
        db.Get<IPasswordResetQueue>().Enqueue(bob.Id, bob.SessionVersion); // asked after alice's send stuck

        await email.StartedAsync(); // bob's send started while alice's still hangs
        await mailer.StopAsync(default);

        Assert.Equal(["bob@example.com"], email.Sent());
        Assert.Single(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(bob.Id, default));
    }

    [Fact]
    public async Task RequestsStillWaitingAtStop_AreNamedInTheLog()
    {
        // Every sender hangs on a request of its own, so the last request is still waiting when the app stops.
        var email = new ScriptedEmail();
        await using var db = Database(email);
        var users = new List<User>();
        for (var i = 0; i <= PasswordResetMailer.Senders; i++)
        {
            users.Add(await AddUser(db, $"user{i}@example.com"));
            email.Scripts[$"user{i}@example.com"] = Hang;
        }
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        foreach (var user in users) db.Get<IPasswordResetQueue>().Enqueue(user.Id, user.SessionVersion);
        await email.StartedAsync(PasswordResetMailer.Senders);
        await mailer.StopAsync(default);

        var lines = db.Log.From<PasswordResetMailer>().Where(e => e.Level == LogLevel.Information).ToList();
        Assert.All(users, user => Assert.Contains(lines, e => Equals(e.Values.GetValueOrDefault("UserId"), user.Id)));
        Assert.True(db.Get<PasswordResetQueue>().WhenIdleAsync().IsCompleted);
        Assert.Empty(email.Sent());
    }

    [Fact]
    public async Task ARequestMadeBeforeANewPassword_SendsNothing()
    {
        var email = new ScriptedEmail();
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        alice.SetPasswordHash("a-new-hash"); // set before the mailer got to the request (it revoked every link)
        await db.Get<IUserRepository>().UpdateAsync(alice, default);
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        await db.Get<PasswordResetQueue>().WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(email.To);
        Assert.Empty(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default));
        await mailer.StopAsync(default);
    }
}
