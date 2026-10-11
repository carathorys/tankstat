using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;
using Tankstat.Infrastructure.Auth;

namespace Tankstat.Infrastructure.UnitTests;

public class PasswordResetMailerTests
{
    /// <summary>Sends to a list; <see cref="Next"/> decides what the next send does instead (throw, or wait until the send is cancelled).</summary>
    private sealed class ScriptedEmail : IEmailSender
    {
        public List<string> To { get; } = [];
        public Func<CancellationToken, Task>? Next { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConfigured => true;

        public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            Started.TrySetResult();
            if (Next is { } next)
            {
                Next = null;
                await next(ct);
            }
            lock (To) To.Add(to);
        }
    }

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
        var email = new ScriptedEmail { Next = _ => throw new InvalidOperationException("the sender is broken") };
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
        var email = new ScriptedEmail { Next = _ => throw new EmailSendException("Sending an e-mail through smtp.test:25 failed (refused)") };
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
        var email = new ScriptedEmail { Next = ct => Task.Delay(Timeout.Infinite, ct) }; // a mail server that never answers
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await email.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
            Next = async ct =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { throw new InvalidOperationException("the connection was closed"); }
            },
        };
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        await email.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await mailer.StopAsync(default);

        Assert.DoesNotContain(db.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Empty(await db.Get<IPasswordResetTokenRepository>().IdsForUserAsync(alice.Id, default));
        Assert.Contains(db.Log.From<PasswordResetMailer>(), e => e.Level == LogLevel.Information && Equals(e.Values.GetValueOrDefault("UserId"), alice.Id));
    }

    [Fact]
    public async Task RequestsStillWaitingAtStop_AreNamedInTheLog()
    {
        var email = new ScriptedEmail { Next = ct => Task.Delay(Timeout.Infinite, ct) }; // alice's send hangs, bob waits behind it
        await using var db = Database(email);
        var alice = await AddUser(db, "alice@example.com");
        var bob = await AddUser(db, "bob@example.com");
        using var mailer = Mailer(db);
        await mailer.StartAsync(default);

        db.Get<IPasswordResetQueue>().Enqueue(alice.Id, alice.SessionVersion);
        db.Get<IPasswordResetQueue>().Enqueue(bob.Id, bob.SessionVersion);
        await email.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await mailer.StopAsync(default);

        var lines = db.Log.From<PasswordResetMailer>().Where(e => e.Level == LogLevel.Information).ToList();
        Assert.Contains(lines, e => Equals(e.Values.GetValueOrDefault("UserId"), alice.Id));
        Assert.Contains(lines, e => Equals(e.Values.GetValueOrDefault("UserId"), bob.Id));
        Assert.True(db.Get<PasswordResetQueue>().WhenIdleAsync().IsCompleted);
        Assert.Empty(email.To);
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
