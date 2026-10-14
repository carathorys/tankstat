using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Api.Backups;

/// <summary>
/// The backup commands of <c>dotnet Tankstat.Api.dll</c> (no server is started; every service exists, no hosted service runs):
/// <c>backup keygen --passphrase-stdin</c> prints the two settings that make backups encrypted and the identity, once;
/// <c>backup [--output &lt;file&gt;] [--photos] [--passphrase-stdin]</c> writes one; <c>decrypt &lt;file&gt; --output &lt;folder&gt;
/// (--identity &lt;file&gt; | --passphrase-stdin)</c> unpacks one. A passphrase is only ever read from the standard input, never from an
/// argument (which the process list and the shell history would keep). Exit code 0, or 1 with the reason on the standard error.
/// </summary>
internal static class BackupCommands
{
    public static bool Handles(string[] args) => args is ["backup", ..] or ["decrypt", ..] or ["restore", ..];

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            return args switch
            {
                ["backup", "keygen", .. var rest] => Keygen(rest, input, output),
                ["backup", .. var rest] => await BackupAsync(rest, services, input, output, error),
                ["decrypt", .. var rest] => Decrypt(rest, input, output),
                ["restore", .. var rest] => await RestoreAsync(rest, services, input, output),
                _ => throw new UsageException(),
            };
        }
        catch (UsageException)
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }
        // What a run can meet is reported, never left to crash the process: settings the host never validated (a command does not start it,
        // so they are validated when first read), a recipient no key agrees with, a backup that is not in the folder, a platform without
        // ChaCha20-Poly1305 (Windows 10), a value the dump cannot hold.
        catch (Exception e) when (e is KeyedException or IOException or UnauthorizedAccessException or FormatException or System.Data.Common.DbException
            or OptionsValidationException or InvalidDataException or CryptographicException or NotSupportedException)
        {
            await error.WriteLineAsync(e.Message); // a database's message may quote the table it misses, as its own errors always did
            return 1;
        }
    }

    private const string Usage = """
        Usage:
          backup keygen --passphrase-stdin                       derive the recipient and the salt from a passphrase (read from the standard input)
          backup [--output <file>] [--photos] [--passphrase-stdin]
                                                                 write a backup (encrypted for Backups:Encryption:Recipient, or for the passphrase read from the standard input)
          decrypt <file> --output <folder> (--identity <file> | --passphrase-stdin)
                                                                 unpack a backup into a folder
          restore <file> [--yes] [--now] [--identity <file> | --passphrase-stdin]
                                                                 replace ALL data with a backup of the backups folder (by file name) at the next start of the
                                                                 server, or right away with --now while the server is NOT running; --yes skips the question
                                                                 (asked on the standard input, before the passphrase line)
        """;

    private static int Keygen(string[] args, TextReader input, TextWriter output)
    {
        if (args is not ["--passphrase-stdin"]) throw new UsageException();
        var passphrase = Passphrase(input);
        var salt = BackupKeys.NewSalt();
        var identity = BackupKeys.Identity(passphrase, salt, Argon2idParameters.Default);
        output.WriteLine("Set these two settings (as environment variables or in the configuration), then every backup is encrypted for this passphrase:");
        output.WriteLine($"  Backups__Encryption__Recipient={AgeKeys.FormatRecipient(BackupKeys.Recipient(identity))}");
        output.WriteLine($"  Backups__Encryption__Salt={Convert.ToBase64String(salt)}");
        output.WriteLine("The identity below opens every backup made for this recipient, with age (age -d -i) or tankstat decrypt --identity; it is derived from the");
        output.WriteLine("passphrase and the salt, so the passphrase alone is enough to keep. Losing the passphrase (or this identity) loses the backups, by design.");
        output.WriteLine("Saved as it is, the next line is an identity file (age takes no space before the key):");
        output.WriteLine(AgeKeys.FormatIdentity(identity));
        return 0;
    }

    private static async Task<int> BackupAsync(string[] args, IServiceProvider services, TextReader input, TextWriter output, TextWriter error)
    {
        string? file = null;
        var photos = false;
        var passphrase = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length && args[i + 1].Length > 0: file = args[++i]; break;
                case "--photos": photos = true; break;
                case "--passphrase-stdin": passphrase = true; break;
                default: throw new UsageException();
            }
        }
        // The server says this at start (SettingsFileReporter, a hosted service, which a command never runs). A backup runs without the file
        // too, so it could come out without what the file held, its encryption say: never without a word.
        if (services.GetService<SettingsFileStatus>() is { Ignored: true } settingsFile)
            await error.WriteLineAsync($"The settings file {settingsFile.Path} was left out because the app could not start with it; the backup is written without it: {string.Join(" ", settingsFile.Problems)}");
        BackupEncryption? encryption = null;
        if (passphrase)
        {
            var salt = BackupKeys.NewSalt();
            var identity = BackupKeys.Identity(Passphrase(input), salt, Argon2idParameters.Default);
            encryption = new BackupEncryption(BackupEncryption.AgeX25519, AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)), Convert.ToBase64String(salt), Argon2idParameters.Default);
        }
        var result = await services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(file, photos ? true : null, encryption), CancellationToken.None);
        var m = result.Manifest;
        output.WriteLine($"Wrote {result.Path}");
        output.WriteLine($"  {m.Contents.Tables.Count} tables with {m.Contents.Tables.Values.Sum()} rows ({m.Sizes.Tables} bytes), pictures {m.Sizes.Pictures} bytes, photos {Photos(m)}, keys {(m.Contents.Keys ? $"{m.Sizes.Keys} bytes" : m.Encryption is null ? "left out (not encrypted)" : "none found")}");
        output.WriteLine(m.Encryption is null ? "  not encrypted" : $"  encrypted for {m.Encryption.Recipient}");
        output.WriteLine($"  settings file {SettingsOf(m)}");
        return 0;
    }

    /// <summary>What a backup holds of the settings file, in words; names only, never a value.</summary>
    private static string SettingsOf(BackupManifest m) => m.Settings switch
    {
        { State: BackupSettingsState.Included, Omitted: { Count: > 0 } omitted } => $"{m.Settings.Bytes} bytes, without {string.Join(", ", omitted)}",
        { State: BackupSettingsState.Included } => $"{m.Settings.Bytes} bytes",
        { State: BackupSettingsState.Ignored } => "left out (the server runs without it)",
        { State: BackupSettingsState.None } => "none (the server has none)",
        _ => "not in the backup (written by an older release)",
    };

    private static string Photos(BackupManifest m) => m.Contents.Photos switch
    {
        BackupPhotos.WithPictures => "with the pictures",
        BackupPhotos.Separate => $"{m.Sizes.Photos} bytes",
        _ => "left out (Backups:Photos is off)",
    };

    private static int Decrypt(string[] args, TextReader input, TextWriter output)
    {
        if (args.Length == 0 || args[0].StartsWith("--")) throw new UsageException();
        var file = args[0];
        string? folder = null, identityFile = null;
        var passphrase = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length: folder = args[++i]; break;
                case "--identity" when i + 1 < args.Length: identityFile = args[++i]; break;
                case "--passphrase-stdin": passphrase = true; break;
                default: throw new UsageException();
            }
        }
        if (folder is null || (identityFile is not null && passphrase)) throw new UsageException();
        var manifest = BackupFile.ReadManifest(file);
        BackupFile.VerifyChecksum(file, manifest);
        byte[]? identity = null;
        if (identityFile is not null) identity = AgeKeys.ParseIdentity(IdentityLine(identityFile));
        else if (passphrase) identity = BackupFile.IdentityOf(manifest, Passphrase(input));
        BackupFile.Extract(file, manifest, identity, folder);
        // The settings file beside the tables, owner-only like the rest: it may hold secrets (an encrypted backup carries them).
        if (BackupFile.ReadSettings(file, manifest, identity) is { } settings)
        {
            var path = System.IO.Path.Combine(folder, BackupSettingsFile.Plain);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = SettingsFileStore.OwnerOnly;
            using (var stream = new FileStream(path, options))
            using (var writer = new StreamWriter(stream))
                writer.Write(SettingsFileStore.ToJson(settings));
        }
        output.WriteLine($"Unpacked {file} into {System.IO.Path.GetFullPath(folder)}: {manifest.Contents.Tables.Count} tables, written by Tankstat {manifest.AppVersion} on {manifest.CreatedAt:u} ({manifest.Provider}, {manifest.Migration}), settings file {SettingsOf(manifest)}");
        return 0;
    }

    private static async Task<int> RestoreAsync(string[] args, IServiceProvider services, TextReader input, TextWriter output)
    {
        if (args.Length == 0 || args[0].StartsWith("--")) throw new UsageException();
        var file = args[0];
        string? identityFile = null;
        var (passphrase, yes, now) = (false, false, false);
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--identity" when i + 1 < args.Length: identityFile = args[++i]; break;
                case "--passphrase-stdin": passphrase = true; break;
                case "--yes": yes = true; break;
                case "--now": now = true; break;
                default: throw new UsageException();
            }
        }
        if (identityFile is not null && passphrase) throw new UsageException();
        var store = services.GetRequiredService<IBackupStore>();
        var manifest = store.ReadManifest(file); // a file of the backups folder, by name
        // A newer release's backup is refused before anything is written, as the app refuses it: the start would refuse it anyway.
        await store.RequireRestorableMigrationAsync(manifest, CancellationToken.None);
        if (!yes)
        {
            output.WriteLine($"This replaces ALL data of this instance (every table, the pictures{(manifest.Contents.Photos == BackupPhotos.Separate ? ", the photos" : "")}{(manifest.Contents.Keys ? ", the keys" : "")}) with the backup {file},");
            output.WriteLine($"written by Tankstat {manifest.AppVersion} on {manifest.CreatedAt:u} ({manifest.Provider}, {manifest.Migration}). No backup of the current data is written: whatever changed since it was made is lost. Type yes to continue:");
            if (input.ReadLine()?.Trim().ToLowerInvariant() != "yes")
            {
                output.WriteLine("Nothing was changed.");
                return 1;
            }
        }
        var identity = identityFile is not null ? IdentityLine(identityFile) : null;
        var opened = store.IdentityFor(manifest, passphrase ? Passphrase(input) : null, identity); // refused here when it does not open
        store.RequestRestore(new RestoreMarker(file, Guid.Empty, DateTimeOffset.UtcNow, opened));
        if (!now)
        {
            output.WriteLine($"The restore of {file} is applied at the next start of the server. Restart it now; a running server shows a notice until then.");
            return 0;
        }
        // Right away, as the server would at its start: the restore, then the migrations; the log above says how it went, and why.
        switch (await StartupSteps.RestoreAndMigrateAsync(services, CancellationToken.None))
        {
            case RestoreOutcome.Applied:
                output.WriteLine($"Restored {file}.");
                return 0;
            case RestoreOutcome.Failed:
                output.WriteLine("The restore failed after the data was replaced, so the files may be half copied; no backup of the data from before it was written, the backup itself is still in the folder to try again, and the log above says why.");
                return 1;
            default:
                output.WriteLine("The restore was not applied, and the data is as it was; the log above says why.");
                return 1;
        }
    }

    /// <summary>The identity in an identity file: its first line that is neither blank nor a comment, as age reads one.</summary>
    private static string IdentityLine(string path) =>
        File.ReadLines(path).FirstOrDefault(l => !l.StartsWith('#') && l.Trim().Length > 0) ?? throw new FormatException("The identity file holds no identity.");

    private static string Passphrase(TextReader input)
    {
        // A byte order mark (a file saved by a Windows editor and piped in) is no part of what was typed: kept, it would derive another key.
        var passphrase = input.ReadLine()?.TrimStart('\uFEFF').TrimEnd('\r');
        if (string.IsNullOrEmpty(passphrase)) throw new DomainException("backup.passphraseRequired", "A passphrase is expected on the standard input (one line).");
        return passphrase;
    }

    private sealed class UsageException : Exception;
}
