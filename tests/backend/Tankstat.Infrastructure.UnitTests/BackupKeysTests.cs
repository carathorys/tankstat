using System.Text;
using Tankstat.Application.Backups;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>A passphrase and a salt always give the same identity and recipient; the production cost is pinned by value.</summary>
public class BackupKeysTests
{
    // Small Argon2id parameters: the tests need the derivation, not its cost (the default is pinned below).
    private static readonly Argon2idParameters Quick = new(256, 1, 1);
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("tankstat-salt-01");

    [Fact]
    public void TheSamePassphraseAndSalt_GiveTheSameKeys_AndTheRecipientOpensTheIdentity()
    {
        var identity = BackupKeys.Identity("correct horse battery staple", Salt, Quick);
        var again = BackupKeys.Identity("correct horse battery staple", Salt, Quick);

        Assert.Equal(32, identity.Length);
        Assert.Equal(identity, again);
        var recipient = AgeKeys.FormatRecipient(BackupKeys.Recipient(identity));
        Assert.StartsWith("age1", recipient);
        Assert.True(BackupKeys.Opens(identity, recipient));
        Assert.False(BackupKeys.Opens(BackupKeys.Identity("Correct horse battery staple", Salt, Quick), recipient));
        Assert.False(BackupKeys.Opens(BackupKeys.Identity("correct horse battery staple", BackupKeys.NewSalt(), Quick), recipient));
        Assert.NotEqual(identity, BackupKeys.Identity("correct horse battery staple", Salt, new Argon2idParameters(512, 1, 1))); // the parameters are part of it
    }

    [Fact]
    public void AKnownVector_AsAgeKeygenWouldSpellIt()
    {
        // Pinned once from this implementation (BouncyCastle's Argon2id, version 1.3), the recipient checked with `age-keygen -y` on the
        // identity: the derivation must not drift, or no older backup could be opened from its passphrase.
        var identity = BackupKeys.Identity("hunter2", Salt, Quick);

        Assert.Equal("AGE-SECRET-KEY-1FNJSM76ZUAVU8MR84HUHYKAPJZD53NCM5CAPJD4YSLVDGCL8TZ2Q6YE2WK", AgeKeys.FormatIdentity(identity));
        Assert.Equal("age173u0n39ku3pu3rhgnwefkanax46vpstsx30pejeyk607pc2xyyaqwxj5w7", AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)));
    }

    [Fact]
    public void TheDefaultCost_Is64MiB_ThreePasses_OneLane_AndANewSaltIs16RandomBytes()
    {
        Assert.Equal(new Argon2idParameters(65536, 3, 1), Argon2idParameters.Default);
        Assert.Equal(16, BackupKeys.NewSalt().Length);
        Assert.NotEqual(BackupKeys.NewSalt(), BackupKeys.NewSalt());
        Assert.Throws<ArgumentException>(() => BackupKeys.Identity("", Salt, Quick));
        Assert.Throws<ArgumentException>(() => BackupKeys.Identity("x", new byte[4], Quick));
    }
}
