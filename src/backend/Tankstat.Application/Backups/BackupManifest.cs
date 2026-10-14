using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tankstat.Application.Backups;

/// <summary>
/// What a backup file says about itself, in plain (<c>manifest.json</c>, the last entry of the outer zip, readable without any key): what
/// release wrote it, from which database and migration, what it holds and how much, the checksum of the inner archive and, when it is
/// encrypted, for which recipient and how the identity is derived from the passphrase; and whether the instance's settings file went in
/// (<see cref="Settings"/>; a member older backups lack, read as null, and older releases skip, so the format stays 1). Nothing personal:
/// no names, e-mails, paths or values.
/// </summary>
public sealed record BackupManifest(
    int Format,
    string WrittenBy,
    string AppVersion,
    DateTimeOffset CreatedAt,
    string Provider,
    string? Migration,
    bool SafetyNet,
    BackupContents Contents,
    BackupSizes Sizes,
    BackupArchive Archive,
    BackupEncryption? Encryption,
    BackupSettingsFile? Settings = null)
{
    public const int CurrentFormat = 1;
    public const string Tankstat = "tankstat";
    public const string FileName = "manifest.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A manifest from its JSON; a <see cref="JsonException"/> when it is not one.</summary>
    public static BackupManifest FromJson(string json) =>
        JsonSerializer.Deserialize<BackupManifest>(json, Json) ?? throw new JsonException("The manifest is empty.");
}

/// <summary>The tables with their row counts, and which files and keys went in.</summary>
public sealed record BackupContents(IReadOnlyDictionary<string, long> Tables, bool Pictures, BackupPhotos Photos, bool Keys);

/// <summary>Where the photos of logs are in the file: with the pictures (one root), as a root of their own, or left out (a root of their own, not asked for).</summary>
public enum BackupPhotos { WithPictures, Separate, LeftOut }

/// <summary>Bytes before compression and encryption, per part, and the inner archive as it is stored in the file.</summary>
public sealed record BackupSizes(long Tables, long Pictures, long Photos, long Keys, long Archive);

/// <summary>The inner archive's entry name in the outer zip and its SHA-256 as stored (so a damaged file is found out before any key is tried).</summary>
public sealed record BackupArchive(string Name, string Sha256)
{
    public const string Encrypted = "data.age";
    public const string Plain = "data.zip";
}

/// <summary>
/// The instance's settings file in the backup: <see cref="BackupSettingsState.Included"/> as an outer entry of its own (<see cref="Entry"/>,
/// encrypted like the data in an encrypted backup), so a restore request reads it without decrypting the data, which can be gigabytes; its
/// SHA-256 as stored, since the archive's checksum does not cover it. <see cref="Omitted"/> names (never values) the keys of the file that
/// stayed out: what is not the instance's but the machine's (a folder, the database, the bootstrap administrator) and, in a plain backup, the
/// secrets.
/// </summary>
public sealed record BackupSettingsFile(BackupSettingsState State, string? Entry = null, string? Sha256 = null, long Bytes = 0, IReadOnlyList<string>? Omitted = null)
{
    public const string Encrypted = "settings.age";
    public const string Plain = "settings.json";
}

/// <summary>Whether the settings file went in: it did, there was none, or the start left it out (the server runs without it, so the backup does too).</summary>
public enum BackupSettingsState { Included, None, Ignored }

/// <summary>How the file is encrypted: age for one X25519 recipient, whose identity is Argon2id of the passphrase and this salt.</summary>
public sealed record BackupEncryption(string Method, string Recipient, string Salt, Argon2idParameters Argon2id)
{
    public const string AgeX25519 = "age-x25519";

    /// <summary>Whether a salt is the base64 of at least 8 bytes, as the identity is derived with it (a setting, or a manifest being read).</summary>
    public static bool IsSalt(string? text) =>
        text is not null && Convert.TryFromBase64String(text.Trim(), new byte[text.Length], out var length) && length >= 8;
}

/// <summary>Argon2id's cost: memory in KiB, passes over it and lanes. Pinned in the manifest, so a later default never locks older files.</summary>
public sealed record Argon2idParameters(int MemoryKiB, int Passes, int Lanes)
{
    /// <summary>64 MiB, 3 passes, 1 lane: about half a second on a server, far more for a guess at the passphrase.</summary>
    public static readonly Argon2idParameters Default = new(64 * 1024, 3, 1);
}
