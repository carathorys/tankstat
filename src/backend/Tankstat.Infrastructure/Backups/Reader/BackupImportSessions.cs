using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// <see cref="IBackupImportSessions"/> on this server's disk and in its memory: a restart, or another server behind the same address,
/// simply asks the person to choose the file again. Every session has a folder under <see cref="Root"/>, emptied whole at start (the app
/// owns that subfolder; a setting gives each server its own place). Opening a file runs in the background: a large one takes longer
/// than a request may, so the page asks how it stands. No passphrase or identity outlives its try.
/// </summary>
internal sealed class BackupImportSessions(
    IOptionsMonitor<BackupImportOptions> options, TimeProvider clock, IHostApplicationLifetime? lifetime, ILogger<BackupImportSessions> logger)
    : IBackupImportSessions, IHostedService, IDisposable
{
    public const string NotABackupKey = "import.notABackup";
    public const string NoTriesLeftKey = "import.noTriesLeft";
    public const string InUseKey = "import.fileInUse";
    public const int MaxTries = 3;
    public const string FolderName = "tankstat-import";
    private const string UploadName = "upload.zip";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    /// <summary>How long an ended session still says why it ended, for the page that is asking.</summary>
    public static readonly TimeSpan EndedFor = TimeSpan.FromMinutes(5);
    /// <summary>The unpacked rows of an upload may be this many times its size: a table's text shrinks about tenfold in the archive.</summary>
    private const int TablesFactor = 20;

    private sealed class Session(string token, Guid userId, BackupFileOrigin origin, string folder, string file, BackupManifest manifest)
    {
        public string Token { get; } = token;
        public Guid UserId { get; } = userId;
        public BackupFileOrigin Origin { get; } = origin;
        public string? Folder { get; set; } = folder;
        public string File { get; } = file;
        public BackupManifest Manifest { get; } = manifest;
        public BackupImportState State { get; set; }
        public int? TriesLeft { get; set; }
        public bool WrongKey { get; set; }
        public ScratchCars? Cars { get; set; }
        public BackupImportRefusal? Refusal { get; set; }
        public DateTimeOffset Expires { get; set; }
        public int Holds { get; set; }
        public CancellationTokenSource Stop { get; } = new();
    }

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private DateTimeOffset Now => clock.GetUtcNow();
    private readonly SemaphoreSlim _derivations = new(1, 1); // each asks for 64 MiB: one at a time on this server
    private readonly object _replacing = new();
    private readonly ConcurrentDictionary<Task, byte> _opening = new(); // the opens under way, a forgotten session's too: a stop waits for them
    private ITimer? _timer;

    /// <summary>The app's own folder inside the scratch place: all of it is this server's to delete.</summary>
    public string Root => Path.Combine(string.IsNullOrWhiteSpace(options.CurrentValue.ScratchFolder) ? Path.GetTempPath() : options.CurrentValue.ScratchFolder, FolderName);

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); // what a stopped server left: no session survives a start
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The import scratch folder could not be emptied at start");
        }
        _timer = clock.CreateTimer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _timer?.Dispose();
        foreach (var session in _sessions.Values) Close(session, null, forget: true);
        // What was being opened winds down first (never throws), as a backup's run does: a restart in the process must not start the
        // next host, whose start empties the scratch folder, while an open of this one still writes into it.
        await WhenIdleAsync().WaitAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>The opens under way, ended ones still winding down too: a stop waits for them, and tests that look at the scratch folder.</summary>
    internal Task WhenIdleAsync() => Task.WhenAll(_opening.Keys);

    public void Dispose()
    {
        _timer?.Dispose();
        _derivations.Dispose();
    }

    public async Task<BackupImportStatus> UploadAsync(Guid userId, Stream content, CancellationToken ct)
    {
        var limit = options.CurrentValue.MaxUploadBytes;
        if (HeldFor(userId)) throw InUse(); // before the body is taken; looked at again when this file replaces the earlier one
        var (token, folder) = NewFolder();
        var file = Path.Combine(folder, UploadName);
        try
        {
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > limit) throw BackupImportService.TooLarge(options.CurrentValue.MaxUploadMb);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            return Begin(token, userId, BackupFileOrigin.Upload, folder, file);
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
    }

    public Task<BackupImportStatus> OpenFileAsync(Guid userId, string path, CancellationToken ct)
    {
        var (token, folder) = NewFolder();
        try
        {
            return Task.FromResult(Begin(token, userId, BackupFileOrigin.RestorePoint, folder, path));
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
    }

    /// <summary>Looks at the file (its manifest only), ends the person's earlier session and starts this one: opening at once, or waiting for the key.</summary>
    private BackupImportStatus Begin(string token, Guid userId, BackupFileOrigin origin, string folder, string file)
    {
        var manifest = Inspect(file);
        var session = new Session(token, userId, origin, folder, file, manifest) { Expires = clock.GetUtcNow() + Lifetime };
        if (manifest.Encryption is null) session.State = BackupImportState.Opening;
        else
        {
            session.State = BackupImportState.NeedsKey;
            session.TriesLeft = origin == BackupFileOrigin.Upload ? MaxTries : null; // a restore point cannot be thrown away (the owner's decision 1)
        }
        // One at a time: two uploads of one person arriving together must leave one session, not two.
        lock (_replacing)
        {
            if (HeldFor(userId)) throw InUse();
            EndForUser(userId);
            _sessions[token] = session;
        }
        logger.LogDebug("Import session {Session} of user {UserId} started from {Origin}", token, userId, origin);
        if (session.State == BackupImportState.Opening) StartOpening(session, null, null);
        return StatusOf(session);
    }

    /// <summary>
    /// The manifest of a file that may be read here, or a keyed refusal: not a backup or an export, written by a newer release (a higher
    /// format, or a migration this release does not know), or asking for more work per passphrase than this release ever writes.
    /// </summary>
    private static BackupManifest Inspect(string file)
    {
        BackupManifest manifest;
        try
        {
            manifest = BackupFile.ReadManifest(file, exports: true);
        }
        catch (DomainException e) when (e.Key == "backup.newerFormat")
        {
            throw NewerRelease(e.Args.GetValueOrDefault("appVersion") as string);
        }
        catch (Exception e) when (e is DomainException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw ScratchCars.NotABackup();
        }
        using (var db = ScratchDatabase.Builder(Path.Combine(Path.GetTempPath(), "unused.db")))
            if (MigrationModels.IdOf(db, manifest.Migration) is null) throw NewerRelease(manifest.AppVersion);
        // The costs are the file's word and are paid before any key is tried: nothing above what this release itself writes is worked on.
        if (manifest.Encryption is { Argon2id: var costs } && (costs.MemoryKiB > Argon2idParameters.Default.MemoryKiB
                || costs.Passes > Argon2idParameters.Default.Passes || costs.Lanes > Argon2idParameters.Default.Lanes))
            throw ScratchCars.NotABackup();
        return manifest;
    }

    private bool HeldFor(Guid userId) => _sessions.Values.Any(s => s.UserId == userId && s.Holds > 0);

    private static DomainException InUse() => new(InUseKey, "An import is still running from the file you opened before.");

    private static DomainException NewerRelease(string? version) =>
        new("backup.newerFormat", "The backup was written by a newer release.", new { AppVersion = BackupReaderText.Version(version) });

    public BackupImportStatus SubmitKey(Guid userId, string token, string? passphrase, string? identity)
    {
        var session = Mine(userId, token) ?? throw BackupImportService.Expired();
        lock (session)
        {
            if (session.State is BackupImportState.Opening or BackupImportState.Opened or BackupImportState.Ended) return StatusOf(session); // one try at a time
            if (session.Origin == BackupFileOrigin.Upload) identity = null; // a person's own upload: its passphrase only (the owner's decision 3)
            if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(identity))
                throw new DomainException("backup.keyRequired", "The backup is encrypted: its passphrase or identity is needed.");
            session.State = BackupImportState.Opening;
            session.WrongKey = false;
            session.Expires = clock.GetUtcNow() + Lifetime;
        }
        StartOpening(session, passphrase, identity);
        return StatusOf(session);
    }

    private void StartOpening(Session session, string? passphrase, string? identity)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(session.Stop.Token, lifetime?.ApplicationStopping ?? CancellationToken.None);
        // Not the caller's context: the request that started it ends long before a large file is open.
        using (ExecutionContext.SuppressFlow())
        {
            var opening = Task.Run(async () =>
            {
                using (stop) await OpenAsync(session, passphrase, identity, stop.Token); // disposed with its try: it hangs on the app's own stopping token
            }, CancellationToken.None);
            _opening[opening] = 0;
            _ = opening.ContinueWith(done => _opening.TryRemove(done, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task OpenAsync(Session session, string? passphrase, string? identity, CancellationToken ct)
    {
        string? folder;
        lock (session) folder = session.Folder;
        if (folder is null) return; // ended before this began
        try
        {
            byte[]? key = null;
            if (session.Manifest.Encryption is { } encryption)
            {
                await _derivations.WaitAsync(ct);
                try
                {
                    key = string.IsNullOrEmpty(identity) ? BackupFile.IdentityOf(session.Manifest, passphrase!) : AgeKeys.ParseIdentity(identity);
                }
                catch (FormatException)
                {
                    throw new WrongKeyException(); // an identity that is none
                }
                finally
                {
                    _derivations.Release();
                }
                bool opens;
                try
                {
                    opens = BackupKeys.Opens(key, encryption.Recipient);
                }
                catch (FormatException)
                {
                    throw ScratchCars.NotABackup(); // a manifest whose recipient is none
                }
                if (!opens) throw new WrongKeyException();
            }
            var o = options.CurrentValue;
            var limits = session.Origin == BackupFileOrigin.Upload
                ? new UnpackLimits(o.MaxUploadBytes, o.MaxUploadBytes * TablesFactor)
                : new UnpackLimits(Math.Max(session.Manifest.Sizes.Archive, new FileInfo(session.File).Length) + 1024 * 1024, session.Manifest.Sizes.Tables + 1024 * 1024);
            await BackupUnpack.VerifyChecksumAsync(session.File, session.Manifest, limits.ArchiveBytes, ct);
            var database = await BackupScratchLoad.RunAsync(
                session.File, session.Manifest, key, folder, limits, keepEmails: session.Origin == BackupFileOrigin.RestorePoint, ct);
            ScratchCars cars;
            try
            {
                cars = await ScratchCars.CreateAsync(database, session.Manifest, ct);
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
            lock (session)
            {
                if (session.State == BackupImportState.Ended) // ended meanwhile (a new upload, the account gone)
                {
                    Discard(cars);
                    return;
                }
                session.Cars = cars;
                session.State = BackupImportState.Opened;
                session.TriesLeft = null;
                session.Expires = clock.GetUtcNow() + Lifetime;
            }
            logger.LogInformation("Import session {Session} of user {UserId} opened a file with {Cars} cars", session.Token, session.UserId, cars.Summary.Cars.Count);
        }
        catch (WrongKeyException)
        {
            lock (session)
            {
                if (session.State == BackupImportState.Ended) return;
                if (session.TriesLeft is { } left && left <= 1)
                {
                    logger.LogInformation("Import session {Session} of user {UserId} ended: three wrong keys", session.Token, session.UserId);
                    Close(session, new BackupImportRefusal(NoTriesLeftKey, new Dictionary<string, object?>()), forget: false);
                    return;
                }
                if (session.TriesLeft is not null) session.TriesLeft--;
                session.WrongKey = true;
                session.State = BackupImportState.NeedsKey;
            }
        }
        catch (OperationCanceledException)
        {
            Close(session, null, forget: true);
        }
        catch (DomainException e) when (e.Key is "backup.newerFormat" or "backup.passphraseEncrypted")
        {
            Close(session, new BackupImportRefusal(e.Key, e.Args), forget: false);
        }
        catch (Exception e)
        {
            // Never the exception's text: a database error quotes values, and here they are the uploader's.
            logger.LogWarning("Import session {Session} of user {UserId}: the file could not be opened ({Failure})", session.Token, session.UserId, e.GetType().Name);
            Close(session, new BackupImportRefusal(NotABackupKey, new Dictionary<string, object?>()), forget: false);
        }
        finally
        {
            // Ended from outside while this still wrote (a new upload, the step left, a stop): the folder's delete ran under it then, and
            // may have missed a file being written, or met one made after it. Nothing writes any more: what is left goes now.
            bool ended;
            lock (session) ended = session.State == BackupImportState.Ended;
            if (ended) DeleteFolder(folder);
        }
    }

    private sealed class WrongKeyException : Exception;

    public BackupImportStatus? Find(Guid userId, string token)
    {
        if (Mine(userId, token) is not { } session) return null;
        lock (session)
        {
            if (session.State != BackupImportState.Ended) session.Expires = clock.GetUtcNow() + Lifetime;
            return StatusOf(session);
        }
    }

    public IBackupCars? Cars(Guid userId, string token)
    {
        if (Mine(userId, token) is not { } session) return null;
        lock (session)
        {
            if (session.State != BackupImportState.Opened) return null;
            session.Expires = clock.GetUtcNow() + Lifetime;
            return session.Cars;
        }
    }

    public IDisposable? Hold(Guid userId, string token)
    {
        if (Mine(userId, token) is not { } session) return null;
        // With the look at the holds of a replacing upload and of the sweep: a hold never lands between that look and the session's end.
        lock (_replacing)
        lock (session)
        {
            if (session.State != BackupImportState.Opened) return null;
            session.Holds++;
        }
        return new Held(this, session);
    }

    private sealed class Held(BackupImportSessions owner, Session session) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            lock (session)
            {
                session.Holds--;
                session.Expires = owner.Now + Lifetime;
            }
        }
    }

    public void End(Guid userId, string token)
    {
        if (Mine(userId, token) is { } session) Close(session, null, forget: true);
    }

    public void EndForUser(Guid userId)
    {
        foreach (var session in _sessions.Values.Where(s => s.UserId == userId).ToList()) Close(session, null, forget: true);
    }

    private Session? Mine(Guid userId, string token) => _sessions.TryGetValue(token, out var session) && session.UserId == userId ? session : null;

    private static BackupImportStatus StatusOf(Session s)
    {
        lock (s) return new BackupImportStatus(s.Token, s.Origin, s.State, s.State == BackupImportState.NeedsKey ? s.TriesLeft : null, s.WrongKey, s.Cars?.Summary, s.Refusal);
    }

    /// <summary>
    /// Ends a session: what is being opened is given up, the cars closed and the folder deleted (an upload with it; a restore point stays
    /// where it is). With a refusal it stays known for a while, to say why to the page that asks; otherwise it is forgotten.
    /// </summary>
    private void Close(Session session, BackupImportRefusal? refusal, bool forget)
    {
        ScratchCars? cars;
        string? folder;
        lock (session)
        {
            if (session.State == BackupImportState.Ended && !forget) return;
            (cars, folder) = (session.Cars, session.Folder);
            session.Cars = null;
            session.Folder = null;
            session.State = BackupImportState.Ended;
            session.Refusal ??= refusal;
            session.Expires = clock.GetUtcNow() + EndedFor;
        }
        try { session.Stop.Cancel(); } catch (ObjectDisposedException) { }
        if (forget) _sessions.TryRemove(session.Token, out _);
        Discard(cars);
        if (folder is not null) DeleteFolder(folder);
    }

    /// <summary>
    /// Deletes the cars' database. Never throws for a file that cannot be deleted (the sweep runs on a timer, where that would end the
    /// process, and the folder must still go): the database is in the session's folder, whose delete follows and says so.
    /// </summary>
    private static void Discard(ScratchCars? cars)
    {
        try
        {
            cars?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the folder's delete, which every caller goes on to, meets the same file and logs it
        }
    }

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var session in _sessions.Values.ToList())
        {
            lock (_replacing) // with Hold: a run that takes the session now either keeps it, or finds it ended
            {
                bool expired;
                lock (session) expired = session.Expires <= now && session.Holds == 0 && session.State != BackupImportState.Opening;
                if (!expired) continue;
                logger.LogDebug("Import session {Session} of user {UserId} expired", session.Token, session.UserId);
                Close(session, null, forget: true);
            }
        }
    }

    /// <summary>For tests: what the timer does every minute.</summary>
    internal void SweepNow() => Sweep();

    private (string Token, string Folder) NewFolder()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var folder = Path.Combine(Root, token);
        // The owner's only: what lands in it is someone's cars in plain.
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
        else
        {
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return (token, folder);
    }

    private void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "An import session's folder could not be deleted; the next start empties the scratch folder");
        }
    }
}
