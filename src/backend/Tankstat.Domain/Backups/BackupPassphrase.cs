namespace Tankstat.Domain.Backups;

/// <summary>
/// The one rule for a passphrase an administrator types for encrypted backups (the Backups page's key set, <c>backup keygen</c>,
/// <c>backup --passphrase-stdin</c>): at least the configured minimum (<c>Backups:PassphraseMinLength</c>, never under <see cref="Floor"/>).
/// A passphrase only opens an existing backup is not judged: it is the one that backup was made with.
/// </summary>
public static class BackupPassphrase
{
    /// <summary>The least a minimum may be, and the minimum when nothing is set.</summary>
    public const int Floor = 12;

    /// <summary>The most a minimum may be.</summary>
    public const int Ceiling = 128;

    public static void Validate(string passphrase, int minLength)
    {
        var min = Math.Max(minLength, Floor);
        if (passphrase.Length < min)
            throw new DomainException("backup.passphraseTooShort", $"A passphrase has at least {min} characters.", new { Min = min });
    }
}
