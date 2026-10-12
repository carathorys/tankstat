using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Api.Backups;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The backup commands of the executable, run against a host's services the way Program hands them over: keygen prints the settings and the
/// identity, backup writes a file of the instance, decrypt opens it with the passphrase or the identity, and a wrong one is refused.
/// </summary>
public sealed class BackupCommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-backups");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed record Run(int Code, string Out, string Error);

    private static async Task<Run> RunAsync(TestApp app, string stdin, params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var code = await BackupCommands.RunAsync(args, app.Factory.Services, new StringReader(stdin), output, error);
        return new Run(code, output.ToString(), error.ToString());
    }

    private static async Task AddVehicle(TestApp app, string name)
    {
        using var client = app.NewClient();
        (await client.Gql("mutation($n: String!) { addVehicle(input: { name: $n, fuelType: PETROL }) { id } }", new { n = name })).Data();
    }

    [Fact]
    public async Task Keygen_PrintsTheTwoSettingsAndTheIdentity_WhichOpensTheRecipient()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None", ["Backups:Path"] = _folder });

        var run = await RunAsync(app, "correct horse battery staple\n", "backup", "keygen", "--passphrase-stdin");

        Assert.Equal((0, ""), (run.Code, run.Error));
        var recipient = Regex.Match(run.Out, @"Backups__Encryption__Recipient=(age1\S+)").Groups[1].Value;
        var salt = Regex.Match(run.Out, @"Backups__Encryption__Salt=(\S+)").Groups[1].Value;
        var identity = Regex.Match(run.Out, @"(AGE-SECRET-KEY-1\S+)").Groups[1].Value;
        Assert.Contains(identity, run.Out.Split('\n').Select(l => l.TrimEnd('\r'))); // a line of its own, as age reads an identity file: no space before it
        Assert.Equal(16, Convert.FromBase64String(salt).Length);
        Assert.True(BackupKeys.Opens(AgeKeys.ParseIdentity(identity), recipient));
        Assert.Contains("Losing the passphrase", run.Out);

        // The same passphrase and salt give the same identity, so the passphrase alone is enough to keep.
        Assert.Equal(AgeKeys.ParseIdentity(identity), BackupKeys.Identity("correct horse battery staple", Convert.FromBase64String(salt), Application.Backups.Argon2idParameters.Default));
        Assert.Equal(1, (await RunAsync(app, "", "backup", "keygen", "--passphrase-stdin")).Code); // no passphrase came
        Assert.Contains("passphrase", (await RunAsync(app, "", "backup", "keygen", "--passphrase-stdin")).Error);
        Assert.Contains("Usage", (await RunAsync(app, "x\n", "backup", "keygen")).Error); // never from an argument
    }

    [Fact]
    public async Task Backup_WritesTheInstance_AndDecrypt_OpensIt_WithThePassphrase_OrTheIdentity_NeverWithAnother()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None", ["Backups:Path"] = _folder });
        await AddVehicle(app, "Backed up car");

        var plain = await RunAsync(app, "", "backup");
        Assert.Equal((0, ""), (plain.Code, plain.Error));
        var file = Assert.Single(Directory.GetFiles(_folder));
        Assert.Contains($"Wrote {file}", plain.Out);
        Assert.Contains("not encrypted", plain.Out);
        Assert.Contains("keys left out", plain.Out);

        var encrypted = await RunAsync(app, "hunter2 hunter2\n", "backup", "--passphrase-stdin", "--output", Path.Combine(_folder, "enc.zip"));
        Assert.Equal((0, ""), (encrypted.Code, encrypted.Error));
        Assert.Contains("encrypted for age1", encrypted.Out);
        var manifest = BackupFile.ReadManifest(Path.Combine(_folder, "enc.zip"));
        Assert.True(manifest.Contents.Keys);
        Assert.Equal(1, manifest.Contents.Tables["Vehicles"]);

        var opened = await RunAsync(app, "hunter2 hunter2\n", "decrypt", Path.Combine(_folder, "enc.zip"), "--output", Path.Combine(_folder, "out"), "--passphrase-stdin");
        Assert.Equal((0, ""), (opened.Code, opened.Error));
        Assert.Contains("Unpacked", opened.Out);
        Assert.Contains("Backed up car", await File.ReadAllTextAsync(Path.Combine(_folder, "out", "tables", "Vehicles.jsonl")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_folder, "out", "keys")));
        if (!OperatingSystem.IsWindows()) // the data and the keys in plain land there: the folder decrypt made is the owner's only
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(_folder, "out")));

        var identityFile = Path.Combine(_folder, "identity.txt");
        await File.WriteAllTextAsync(identityFile, "# created by the test\n" + AgeKeys.FormatIdentity(BackupFile.IdentityOf(manifest, "hunter2 hunter2")) + "\n");
        var byIdentity = await RunAsync(app, "", "decrypt", Path.Combine(_folder, "enc.zip"), "--output", Path.Combine(_folder, "out2"), "--identity", identityFile);
        Assert.Equal((0, ""), (byIdentity.Code, byIdentity.Error));
        Assert.True(File.Exists(Path.Combine(_folder, "out2", "tables", "Vehicles.jsonl")));

        // A passphrase piped from a file a Windows editor saved starts with a byte order mark, which is no part of it.
        var withMark = await RunAsync(app, "\uFEFFhunter2 hunter2\r\n", "decrypt", Path.Combine(_folder, "enc.zip"), "--output", Path.Combine(_folder, "bom"), "--passphrase-stdin");
        Assert.Equal((0, ""), (withMark.Code, withMark.Error));

        var wrong = await RunAsync(app, "hunter3\n", "decrypt", Path.Combine(_folder, "enc.zip"), "--output", Path.Combine(_folder, "out3"), "--passphrase-stdin");
        Assert.Equal(1, wrong.Code);
        Assert.Contains("does not open", wrong.Error);
        Assert.False(Directory.Exists(Path.Combine(_folder, "out3", "tables"))); // refused before anything was unpacked

        var noKey = await RunAsync(app, "", "decrypt", Path.Combine(_folder, "enc.zip"), "--output", Path.Combine(_folder, "out4"));
        Assert.Equal(1, noKey.Code);
        var plainOpened = await RunAsync(app, "", "decrypt", file, "--output", Path.Combine(_folder, "out5"));
        Assert.Equal((0, ""), (plainOpened.Code, plainOpened.Error)); // a plain backup needs no key
        Assert.False(Directory.Exists(Path.Combine(_folder, "out5", "keys")));

        // Neither the passphrase nor an identity reaches the log; the file names do, by name only.
        Assert.False(app.Log.Mentions("hunter2"));
        Assert.False(app.Log.Mentions("AGE-SECRET-KEY"));
        Assert.False(app.Log.Mentions(_folder));
    }

    [Fact]
    public async Task ADamagedFile_AndAFileThatIsNoBackup_AreRefusedWithAReason()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None", ["Backups:Path"] = _folder });
        Directory.CreateDirectory(_folder);
        var notAZip = Path.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(notAZip, "not a backup");
        var run = await RunAsync(app, "", "decrypt", notAZip, "--output", Path.Combine(_folder, "out"));
        Assert.Equal(1, run.Code);
        Assert.Contains("not a Tankstat backup", run.Error);

        var made = await RunAsync(app, "", "backup", "--output", Path.Combine(_folder, "ok.zip"));
        Assert.Equal(0, made.Code);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_folder, "ok.zip"));
        bytes[200] ^= 0xff; // inside the inner archive: the checksum catches it
        await File.WriteAllBytesAsync(Path.Combine(_folder, "damaged.zip"), bytes);
        var damaged = await RunAsync(app, "", "decrypt", Path.Combine(_folder, "damaged.zip"), "--output", Path.Combine(_folder, "out2"));
        Assert.Equal(1, damaged.Code);
        Assert.Contains("damaged", damaged.Error);

        // The first entry's local header (its signature at the very start): the central directory, and so the manifest, still read fine.
        bytes = await File.ReadAllBytesAsync(Path.Combine(_folder, "ok.zip"));
        bytes[0] ^= 0xff;
        await File.WriteAllBytesAsync(Path.Combine(_folder, "header.zip"), bytes);
        var header = await RunAsync(app, "", "decrypt", Path.Combine(_folder, "header.zip"), "--output", Path.Combine(_folder, "out3"));
        Assert.Equal((1, true), (header.Code, header.Error.Contains("damaged")));

        // Someone else's zip with a manifest.json of its own (a browser extension's) reads as a manifest with nothing set.
        var extension = Path.Combine(_folder, "extension.zip");
        using (var zip = ZipFile.Open(extension, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            await writer.WriteAsync("""{ "manifest_version": 3, "name": "Some extension", "version": "1.0" }""");
        var foreign = await RunAsync(app, "", "decrypt", extension, "--output", Path.Combine(_folder, "out4"));
        Assert.Equal((1, true), (foreign.Code, foreign.Error.Contains("not a Tankstat backup")));
    }

    [Fact]
    public async Task SettingsTheHostNeverValidated_AndALeftOutSettingsFile_AreReportedWithExitCode1_NotACrash()
    {
        // A command never starts the host, so ValidateOnStart never runs: a setting is validated when the writer first reads it.
        var refusal = new OptionsValidationException("", typeof(BackupOptions),
            ["Backups:Encryption:Recipient and Backups:Encryption:Salt are set together (tankstat backup keygen prints both) or not at all."]);
        using var services = new ServiceCollection()
            .AddSingleton(new SettingsFileStatus("/data/settings.json", Exists: true, Ignored: true, ["Smtp:Port must be a whole number."]))
            .AddSingleton<IBackupWriter>(new FailingWriter(refusal))
            .BuildServiceProvider();
        var (output, error) = (new StringWriter(), new StringWriter());

        var code = await BackupCommands.RunAsync(["backup"], services, new StringReader(""), output, error);

        Assert.Equal(1, code);
        Assert.Contains("The settings file /data/settings.json was left out", error.ToString()); // the backup would be written without it
        Assert.Contains("Smtp:Port", error.ToString());
        Assert.Contains("set together", error.ToString());
        Assert.Contains("Usage", await Error(services, "backup", "--output", "")); // an empty file name is no file name
    }

    private static async Task<string> Error(IServiceProvider services, params string[] args)
    {
        var error = new StringWriter();
        await BackupCommands.RunAsync(args, services, new StringReader(""), new StringWriter(), error);
        return error.ToString();
    }

    private sealed class FailingWriter(Exception failure) : IBackupWriter
    {
        public Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct) => Task.FromException<BackupResult>(failure);
    }
}
