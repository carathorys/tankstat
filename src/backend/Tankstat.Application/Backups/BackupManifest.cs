using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tankstat.Application.Backups;

/// <summary>
/// What a backup file says about itself, in plain (<c>manifest.json</c>, the last entry of the outer zip, readable without any key): what
/// release wrote it, from which database and migration, what it holds and how much, the checksum of the inner archive and, when it is
/// encrypted, for which recipient and how the identity is derived from the passphrase. Nothing personal: no names, e-mails or paths.
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
    BackupEncryption? Encryption)
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

/// <summary>How the file is encrypted: age for one X25519 recipient, whose identity is Argon2id of the passphrase and this salt.</summary>
public sealed record BackupEncryption(string Method, string Recipient, string Salt, Argon2idParameters Argon2id)
{
    public const string AgeX25519 = "age-x25519";
}

/// <summary>Argon2id's cost: memory in KiB, passes over it and lanes. Pinned in the manifest, so a later default never locks older files.</summary>
public sealed record Argon2idParameters(int MemoryKiB, int Passes, int Lanes)
{
    /// <summary>64 MiB, 3 passes, 1 lane: about half a second on a server, far more for a guess at the passphrase.</summary>
    public static readonly Argon2idParameters Default = new(64 * 1024, 3, 1);
}
