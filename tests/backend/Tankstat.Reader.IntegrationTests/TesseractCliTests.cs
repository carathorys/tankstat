using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Ocr;

namespace Tankstat.Reader.IntegrationTests;

/// <summary>The Tesseract runner against a stand-in script, so arguments, parsing, failures and timeouts are checked without Tesseract.</summary>
public sealed class TesseractCliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tankstat-tesseract-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
        + "1\t1\t0\t0\t0\t0\t0\t0\t600\t800\t-1\t\n"
        + "5\t1\t1\t1\t1\t1\t10\t10\t80\t20\t96\tTOTAL\n"
        + "5\t1\t1\t1\t1\t2\t500\t10\t60\t20\t93\t12.50\n";

    /// <summary>A stand-in for tesseract: answers --version and --list-langs, otherwise records its arguments, swallows stdin and prints <paramref name="body"/>.</summary>
    private string Script(string body = "cat \"$here/page.tsv\"", string version = "echo 'tesseract 5.4.1'; echo ' leptonica-1.84.1'")
    {
        File.WriteAllText(Path.Combine(_dir, "page.tsv"), Tsv);
        var path = Path.Combine(_dir, "tesseract");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            here="$(dirname "$0")"
            for a in "$@"; do
              if [ "$a" = "--version" ]; then {{version}}; exit 0; fi
              if [ "$a" = "--list-langs" ]; then echo 'List of available languages in "/x/" (3):'; echo eng; echo hun; echo osd; exit 0; fi
            done
            printf '%s\n' "$@" > "$here/args.txt"
            cat > /dev/null
            {{body}}
            """.Replace("\r\n", "\n"));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static TesseractCli Cli(string executable, int timeout = 20, string? tessdata = null) => new(
        Options.Create(new ReaderOptions { Tesseract = new TesseractOptions { Executable = executable, TimeoutSeconds = timeout, TessdataPath = tessdata } }),
        TimeProvider.System, NullLogger<TesseractCli>.Instance);

    private static TesseractCliEngine Engine(TesseractCli cli, int timeout = 20) =>
        new(cli, Options.Create(new ReaderOptions { Tesseract = new TesseractOptions { TimeoutSeconds = timeout } }), NullLogger<TesseractCliEngine>.Instance);

    [UnixFact]
    public void TheProbe_FindsTheVersion_AndKeepsOnlyInstalledLanguages()
    {
        var info = Cli(Script()).Info();

        Assert.True(info.Available);
        Assert.Equal("5.4.1", info.Version);
        Assert.Equal(("hun+eng", "eng"), (info.TextLanguages, info.DigitLanguages)); // deu is not installed
        Assert.Equal(["hun", "eng"], info.Languages);
    }

    [UnixFact]
    public void AMissingTesseract_IsUnavailable_NotACrash()
    {
        var info = Cli(Path.Combine(_dir, "no-such-tesseract")).Info();

        Assert.False(info.Available);
        Assert.NotNull(info.Problem);
    }

    [UnixFact]
    public async Task APass_SendsTheImage_WithTheRightArguments_AndParsesTheTsv()
    {
        var engine = Engine(Cli(Script(), tessdata: "/opt/tessdata"));

        var page = await engine.RecognizeAsync(new byte[] { 1, 2, 3 }, new OcrPass(ImageVariant.Inverted, 11, OcrPurpose.Digits), default);

        Assert.Equal(["TOTAL", "12.50"], page.Words.Select(w => w.Text));
        Assert.Equal(["stdin", "stdout", "-l", "eng", "--psm", "11", "--tessdata-dir", "/opt/tessdata", "-c", "tessedit_char_whitelist=0123456789", "tsv"],
            File.ReadAllLines(Path.Combine(_dir, "args.txt")));
    }

    [UnixFact]
    public async Task ATextPass_UsesTheReceiptLanguages_WithoutTheDigitFilter()
    {
        await Engine(Cli(Script())).RecognizeAsync(new byte[] { 1 }, new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text), default);

        Assert.Equal(["stdin", "stdout", "-l", "hun+eng", "--psm", "6", "tsv"], File.ReadAllLines(Path.Combine(_dir, "args.txt")));
    }

    [UnixFact]
    public async Task AFailingPass_ReadsAsAnEmptyPage()
    {
        var engine = Engine(Cli(Script("echo 'Error in pixReadMem' >&2; exit 1")));

        var page = await engine.RecognizeAsync(new byte[] { 1 }, new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text), default);

        Assert.Empty(page.Words);
    }

    [UnixFact]
    public async Task ASlowPass_IsKilledAtTheTimeout_AndReadsAsAnEmptyPage()
    {
        var engine = Engine(Cli(Script("sleep 30")), timeout: 1);
        var started = DateTime.UtcNow;

        var page = await engine.RecognizeAsync(new byte[] { 1 }, new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text), default);

        Assert.Empty(page.Words);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [UnixFact]
    public async Task WithoutTesseract_APassSaysTheOcrIsUnavailable()
    {
        var engine = Engine(Cli(Path.Combine(_dir, "no-such-tesseract")));

        var error = await Assert.ThrowsAsync<Reading.ReaderException>(() => engine.RecognizeAsync(new byte[] { 1 }, new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text), default));

        Assert.Equal("ocr_unavailable", error.Code);
    }

    [TesseractFact]
    public void TheRealTesseract_IsFound()
    {
        var info = Cli("tesseract").Info();

        Assert.True(info.Available, info.Problem);
        Assert.Contains("eng", info.Languages);
    }
}
