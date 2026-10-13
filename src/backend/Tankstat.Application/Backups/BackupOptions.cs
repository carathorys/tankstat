using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Backups;

namespace Tankstat.Application.Backups;

/// <summary>
/// Bound from the "Backups" section (<c>Backups__Path=/data/backups</c>, <c>Backups__Photos=true</c>, <c>Backups__Encryption__Recipient=age1...</c>,
/// <c>Backups__Encryption__Salt=...</c>). The folder is an environment-level setting with a default, like <c>Storage:Path</c>; the rest may
/// come from any configuration source.
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

    /// <summary>The recipient backups are encrypted for, and the salt the identity is derived with; unset, backups are written in plain (without the keys).</summary>
    public BackupEncryptionOptions Encryption { get; set; } = new();

    /// <summary><see cref="Path"/> in full, without a trailing separator.</summary>
    public string Root => Folders.Full(Path);

    public bool Encrypted => !string.IsNullOrWhiteSpace(Encryption.Recipient);
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
