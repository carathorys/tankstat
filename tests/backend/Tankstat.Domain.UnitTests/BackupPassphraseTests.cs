using Tankstat.Domain.Backups;

namespace Tankstat.Domain.UnitTests;

public class BackupPassphraseTests
{
    [Fact]
    public void APassphrase_NeedsTheMinimumItIsGiven_NeverLessThanTheFloor()
    {
        BackupPassphrase.Validate("twelve chars", 12);

        var underTheMinimum = Assert.Throws<DomainException>(() => BackupPassphrase.Validate("correct horse battery staple", 30));
        Assert.Equal(("backup.passphraseTooShort", (object?)30), (underTheMinimum.Key, underTheMinimum.Args["min"]));
        var underTheFloor = Assert.Throws<DomainException>(() => BackupPassphrase.Validate("eleven char", 8)); // a minimum below 12 counts as 12
        Assert.Equal((object?)12, underTheFloor.Args["min"]);
    }
}
