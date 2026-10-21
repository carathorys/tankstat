using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Backups;

namespace Tankstat.Application.Backups;

/// <summary>
/// Bound from the "Backups" section (<c>Backups__Path=/data/backups</c>, <c>Backups__Photos=true</c>, <c>Backups__Encryption__Recipient=age1...</c>,
/// <c>Backups__Encryption__Salt=...</c>, <c>Backups__Every=02:00</c>, <c>Backups__Days=Monday,Thursday</c>, <c>Backups__Keep=14</c>, <c>Backups__KeepDays=30</c>). The folder is an
/// environment-level setting with a default, like <c>Storage:Path</c>; the rest may come from any configuration source.
/// </summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backups";

    /// <summary>Where backups are written (and, later, read from as restore points). Relative paths start at the working directory.</summary>
    public string Path { get; set; } = "backups";

    /// <summary>
    /// Whether the photos of logs go in when they have a root of their own (<c>Storage:PhotosPath</c>); they grow without limit, so they are
    /// left out unless asked for. With one root (the default) they are always in the file with the pictures, and this has no meaning.
    /// </summary>
    public bool Photos { get; set; }

    /// <summary>
    /// What a backup does with the settings environment variables hold (#140): <see cref="BackupEnvironmentMode.Record"/> names them in the
    /// manifest only (nothing leaves the environment), <see cref="BackupEnvironmentMode.Merge"/> also puts the values the server runs with into
    /// the backup's copy of the settings file (never into the live one), so a restore elsewhere brings them.
    /// </summary>
    public BackupEnvironmentMode Environment { get; set; } = BackupEnvironmentMode.Record;

    /// <summary>
    /// Whether a backup's settings file holds the secrets (the OIDC client secret, the SMTP password, the model API key), encrypted or not
    /// (user decision: only when asked for, whatever the backup). Off by default. In a plain backup anyone who has the file reads them, which
    /// the settings page says while it is on and backups are not encrypted.
    /// </summary>
    public bool Secrets { get; set; }

    /// <summary>The recipient backups are encrypted for, and the salt the identity is derived with; unset, backups are written in plain (without the keys).</summary>
    public BackupEncryptionOptions Encryption { get; set; } = new();

    /// <summary>
    /// The least a passphrase typed for encrypted backups must have (12 to 128, <see cref="BackupPassphrase"/>): the Backups page's key set and
    /// the command line's alike. A made-up one has 20 characters of 100 random bits whatever this says.
    /// </summary>
    public int PassphraseMinLength { get; set; } = BackupPassphrase.Floor;

    /// <summary>The largest backup an administrator may upload on the page, in MB (a file with photos can be gigabytes: copying it into the folder always works too).</summary>
    public int MaxUploadMb { get; set; } = 1024;

    public long MaxUploadBytes => MaxUploadMb * 1024L * 1024L;

    /// <summary>
    /// The times of day (<c>HH:mm</c>, the server's local time: <c>TZ</c> in a container; several with commas between them) a backup is
    /// written on each day of <see cref="Days"/>; unset, nothing is scheduled. See <see cref="BackupSchedule"/>.
    /// </summary>
    public string? Every { get; set; }

    /// <summary>
    /// The days of the week a backup is written on (<c>Monday,Thursday</c>; the whole week by default); <c>None</c> chooses no day, so
    /// nothing is scheduled. See <see cref="BackupSchedule.ParseDays"/>.
    /// </summary>
    public string? Days { get; set; } = BackupSchedule.EveryDay;

    /// <summary>After a backup the app wrote, keep only the newest this many of its own (unset: no limit by count). See <see cref="BackupRetention"/>.</summary>
    public int? Keep { get; set; }

    /// <summary>After a backup the app wrote, remove its own older than this many days (unset: no limit by age). See <see cref="BackupRetention"/>.</summary>
    public int? KeepDays { get; set; }

    /// <summary>The schedule of <see cref="Every"/> and <see cref="Days"/>, or null when nothing is scheduled.</summary>
    public BackupSchedule? Schedule => BackupSchedule.Of(Every, Days);

    public bool Pruning => Keep is not null || KeepDays is not null;

    /// <summary><see cref="Path"/> in full, without a trailing separator.</summary>
    public string Root => Folders.Full(Path);

    public bool Encrypted => !string.IsNullOrWhiteSpace(Encryption.Recipient);

    /// <summary>The recipient new backups are encrypted for, as their manifests carry it (trimmed: a secret file's line break is no part of it); null in plain.</summary>
    public string? CurrentRecipient => Encrypted ? Encryption.Recipient!.Trim() : null;
}

public sealed class BackupEncryptionOptions
{
    /// <summary>An age X25519 recipient (<c>age1...</c>), as <c>tankstat backup keygen</c> prints it.</summary>
    public string? Recipient { get; set; }

    /// <summary>The salt (base64) the identity is derived from the passphrase with; printed together with the recipient.</summary>
    public string? Salt { get; set; }
}

public sealed class BackupOptionsValidator(IOptions<StorageOptions> storage) : IValidateOptions<BackupOptions>
{
    public ValidateOptionsResult Validate(string? name, BackupOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Path)) return ValidateOptionsResult.Fail("Backups:Path must not be empty.");
        var failures = new List<string>();
        // A backup takes everything below a storage root; the backups folder may not be in there, nor hold one (it would back itself up).
        foreach (var (setting, root) in Roots())
            if (Folders.Overlap(o.Root, root)) failures.Add($"Backups:Path and {setting} must be two separate folders, not the same one or one inside the other.");
        var (recipient, salt) = (o.Encryption.Recipient, o.Encryption.Salt);
        if (string.IsNullOrWhiteSpace(recipient) != string.IsNullOrWhiteSpace(salt))
            failures.Add("Backups:Encryption:Recipient and Backups:Encryption:Salt are set together (tankstat backup keygen prints both) or not at all.");
        if (!string.IsNullOrWhiteSpace(recipient) && !IsRecipient(recipient)) failures.Add("Backups:Encryption:Recipient is not an age recipient (age1...).");
        if (!string.IsNullOrWhiteSpace(salt) && !BackupEncryption.IsSalt(salt)) failures.Add("Backups:Encryption:Salt is not the base64 of at least 8 bytes.");
        if (o.MaxUploadMb < 1) failures.Add("Backups:MaxUploadMb must be at least 1.");
        if (o.PassphraseMinLength is < BackupPassphrase.Floor or > BackupPassphrase.Ceiling)
            failures.Add($"Backups:PassphraseMinLength must be from {BackupPassphrase.Floor} to {BackupPassphrase.Ceiling}.");
        if (BackupSchedule.ParseTimes(o.Every) is null) failures.Add("Backups:Every must be one or more times of day such as 02:00 or 02:00, 14:00 (HH:mm).");
        if (BackupSchedule.ParseDays(o.Days) is null) failures.Add("Backups:Days must be days of the week such as Monday,Thursday, or None.");
        if (o.Keep < 1) failures.Add("Backups:Keep must be at least 1 (the newest backup is always kept).");
        if (o.KeepDays < 1) failures.Add("Backups:KeepDays must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<(string Setting, string Root)> Roots()
    {
        StorageOptions s;
        try
        {
            s = storage.Value;
        }
        catch (OptionsValidationException)
        {
            yield break; // reported by its own validator
        }
        yield return ("Storage:Path", s.PicturesRoot);
        if (!s.OneRoot) yield return ("Storage:PhotosPath", s.PhotosRoot);
    }

    private static bool IsRecipient(string text)
    {
        try
        {
            AgeKeys.ParseRecipient(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>What a backup does with the settings environment variables hold (<see cref="BackupOptions.Environment"/>).</summary>
public enum BackupEnvironmentMode { Record, Merge }
