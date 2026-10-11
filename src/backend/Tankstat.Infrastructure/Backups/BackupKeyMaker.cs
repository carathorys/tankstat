using System.Security.Cryptography;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// A key set for encrypted backups, the same as <c>tankstat backup keygen</c> makes: Argon2id of the passphrase and a new salt. A passphrase
/// that is made up is 20 characters in five groups (<c>K7QM-4XWD-...</c>, from 32 letters and digits that cannot be mistaken for each other:
/// 100 random bits); one the administrator types needs at least <see cref="MinLength"/> characters.
/// </summary>
internal sealed class BackupKeyMaker : IBackupKeyMaker
{
    public const int MinLength = 12;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    public BackupKeySet Make(string? passphrase)
    {
        var generated = string.IsNullOrWhiteSpace(passphrase);
        var phrase = generated ? NewPassphrase() : passphrase!;
        if (phrase.Length < MinLength) throw new DomainException("backup.passphraseTooShort", $"A passphrase has at least {MinLength} characters.", new { Min = MinLength });
        var salt = BackupKeys.NewSalt();
        var identity = BackupKeys.Identity(phrase, salt, Argon2idParameters.Default);
        return new BackupKeySet(phrase, generated, AgeKeys.FormatIdentity(identity), AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)), Convert.ToBase64String(salt));
    }

    internal static string NewPassphrase() =>
        string.Join('-', Enumerable.Range(0, 5).Select(_ => RandomNumberGenerator.GetString(Alphabet, 4)));
}
