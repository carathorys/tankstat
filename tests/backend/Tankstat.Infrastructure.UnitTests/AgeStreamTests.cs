using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The backup encryption is the age format exactly: files age 1.3.2 wrote (tests/backend/contracts/age) are read here, and what is
/// written here is opened by the real tool when it is at hand (<c>age</c> on the PATH, or through mise), else that check says it was skipped.
/// </summary>
public sealed class AgeStreamTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "contracts", "age", name);

    private static byte[] IdentityOf(string file) =>
        AgeKeys.ParseIdentity(File.ReadLines(Fixture(file)).First(l => !l.StartsWith('#')));

    private static readonly byte[] Identity = IdentityOf("identity.txt");
    private static readonly byte[] OtherIdentity = IdentityOf("other-identity.txt");
    private static readonly string Recipient = File.ReadAllText(Fixture("recipient.txt")).Trim();

    private static byte[] Decrypt(byte[] file, byte[] identity)
    {
        using var reader = AgeStream.Decrypt(new MemoryStream(file), identity);
        using var plain = new MemoryStream();
        reader.CopyTo(plain);
        return plain.ToArray();
    }

    private static byte[] Encrypt(byte[] plain, byte[] recipient, int writeSize = 1000)
    {
        var file = new MemoryStream();
        using (var writer = AgeStream.Encrypt(file, recipient, leaveOpen: true))
            for (var offset = 0; offset < plain.Length; offset += writeSize) writer.Write(plain, offset, Math.Min(writeSize, plain.Length - offset));
        return file.ToArray();
    }

    private static string KeyOf(Action action) => Assert.Throws<DomainException>(action).Key;

    [Theory]
    [InlineData("plain.txt")]
    [InlineData("plain-70000.bin")] // a full chunk and a final one
    public void AFileAgeWrote_IsReadBackExactly(string name)
    {
        Assert.Equal(File.ReadAllBytes(Fixture(name)), Decrypt(File.ReadAllBytes(Fixture(name + ".age")), Identity));
    }

    [Fact]
    public void TheIdentityOfAnotherRecipient_DoesNotOpenIt()
    {
        Assert.Equal("backup.wrongKey", KeyOf(() => Decrypt(File.ReadAllBytes(Fixture("plain.txt.age")), OtherIdentity)));
    }

    [Fact]
    public void AgesOwnPassphraseMode_IsRefusedAsSuch()
    {
        Assert.Equal("backup.passphraseEncrypted", KeyOf(() => Decrypt(File.ReadAllBytes(Fixture("passphrase.age")), Identity)));
    }

    [Fact]
    public void ADamagedFile_IsRefused_WhereverTheDamageIs()
    {
        var file = File.ReadAllBytes(Fixture("plain-70000.bin.age"));
        var head = Encoding.ASCII.GetString(file, 0, 400);
        var macStart = head.IndexOf("\n--- ", StringComparison.Ordinal) + 5;
        var headerEnd = head.IndexOf('\n', macStart) + 1;

        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(Flipped(file, macStart + 3), Identity))); // in the MAC
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(Flipped(file, headerEnd + 100), Identity))); // in the first chunk
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(Flipped(file, file.Length - 5), Identity))); // in the last chunk's tag
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(file[..(file.Length - 1)], Identity))); // cut short
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(file[..(headerEnd + AgeFormat.PayloadNonceLength)], Identity))); // no payload at all
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(Encoding.ASCII.GetBytes("PK\x03\x04 a zip, not age"), Identity)));
        Assert.Equal("backup.wrongKey", KeyOf(() => Decrypt(Flipped(file, Encoding.ASCII.GetBytes("age-encryption.org/v1\n-> X25519 ").Length + 2), Identity))); // in the share: no stanza opens
        Assert.Equal(File.ReadAllBytes(Fixture("plain-70000.bin")), Decrypt(file, Identity)); // the original is untouched
    }

    private static byte[] Flipped(byte[] file, int at)
    {
        var copy = (byte[])file.Clone();
        copy[at] ^= 0x01;
        return copy;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(AgeFormat.ChunkSize - 1)]
    [InlineData(AgeFormat.ChunkSize)] // exactly one full chunk, which is then the final one
    [InlineData(AgeFormat.ChunkSize + 1)]
    [InlineData(3 * AgeFormat.ChunkSize + 12345)]
    public void WhatIsWritten_IsReadBack_WhateverTheSize(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        var recipient = AgeKeys.ParseRecipient(Recipient);

        var file = Encrypt(plain, recipient, writeSize: 777);

        var text = Encoding.ASCII.GetString(file, 0, Math.Min(file.Length, 200));
        Assert.StartsWith("age-encryption.org/v1\n-> X25519 ", text);
        Assert.Equal(file.Length, Encrypt(plain, recipient, writeSize: AgeFormat.ChunkSize * 2).Length); // the chunking does not depend on how it was written
        Assert.Equal(plain, Decrypt(file, Identity));
        Assert.Equal("backup.wrongKey", KeyOf(() => Decrypt(file, OtherIdentity)));
    }

    [Fact]
    public void TheWriter_IsForwardOnly_AndTheReaderCountsChunksLikeTheTool()
    {
        var recipient = AgeKeys.ParseRecipient(Recipient);
        using var writer = AgeStream.Encrypt(new MemoryStream(), recipient);
        Assert.False(writer.CanSeek);
        Assert.False(writer.CanRead);
        Assert.True(writer.CanWrite);

        // A file whose final chunk is empty although there was a chunk before it: the tool refuses it, and so does the reader.
        var file = Encrypt(new byte[AgeFormat.ChunkSize], recipient);
        var withEmptyFinal = new MemoryStream();
        withEmptyFinal.Write(file);
        withEmptyFinal.Write(new byte[AgeFormat.TagLength]);
        Assert.Equal("backup.corrupt", KeyOf(() => Decrypt(withEmptyFinal.ToArray(), Identity)));
    }

    [Fact]
    public async Task TheRealTool_OpensWhatTheWriterWrote()
    {
        var age = AgeCommand();
        if (age is null)
        {
            output.WriteLine("Skipped: age is not on the PATH (and not through mise); the writer's file was not checked with the real tool.");
            return;
        }
        var folder = Directory.CreateTempSubdirectory("tankstat-age-");
        try
        {
            var plain = RandomNumberGenerator.GetBytes(AgeFormat.ChunkSize + 4321);
            await File.WriteAllBytesAsync(Path.Combine(folder.FullName, "backup.age"), Encrypt(plain, AgeKeys.ParseRecipient(Recipient)));

            var (code, log) = await Run(age, ["-d", "-i", Fixture("identity.txt"), "-o", Path.Combine(folder.FullName, "plain.bin"), Path.Combine(folder.FullName, "backup.age")], folder.FullName);

            Assert.True(code == 0, log);
            Assert.Equal(plain, await File.ReadAllBytesAsync(Path.Combine(folder.FullName, "plain.bin")));
            output.WriteLine($"age -d opened the writer's file ({log.Trim()})");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>The command that runs age here: <c>age</c> itself, or mise's copy; null when neither answers <c>--version</c>.</summary>
    private static string[]? AgeCommand()
    {
        foreach (var command in new[] { new[] { "age" }, new[] { "mise", "exec", "age@1.3.2", "--", "age" } })
        {
            try
            {
                if (Run(command, ["--version"], Path.GetTempPath()).GetAwaiter().GetResult().Code == 0) return command;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // not installed
            }
        }
        return null;
    }

    private static async Task<(int Code, string Log)> Run(string[] command, string[] args, string workingDirectory)
    {
        var info = new ProcessStartInfo(command[0]) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var part in command.Skip(1).Concat(args)) info.ArgumentList.Add(part);
        using var process = Process.Start(info)!;
        var log = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, log);
    }
}
