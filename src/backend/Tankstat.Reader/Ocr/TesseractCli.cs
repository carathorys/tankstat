using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Reading;

namespace Tankstat.Reader.Ocr;

/// <summary>What the installed Tesseract offers: its version, its languages, and the language sets the passes use.</summary>
public sealed record TesseractInfo(bool Available, string? Version, IReadOnlyList<string> Languages, string TextLanguages, string DigitLanguages, string? Problem);

/// <summary>Whether the OCR can be run; the health check asks it (and tests replace it).</summary>
public interface IOcrStatus
{
    TesseractInfo Info();
}

/// <summary>
/// Runs the Tesseract command line: the image on standard input, TSV on standard output, one thread (the reader runs several photos
/// in parallel instead), killed when it takes too long. What is installed is probed once; after a failed probe again at most every
/// 30 seconds, so a missing Tesseract shows in the health check instead of crash-looping the service.
/// </summary>
internal sealed class TesseractCli(IOptions<ReaderOptions> options, TimeProvider clock, ILogger<TesseractCli> logger) : IOcrStatus
{
    private readonly Lock _gate = new();
    private TesseractInfo? _info;
    private DateTimeOffset _probedAt;

    private TesseractOptions Settings => options.Value.Tesseract;

    public TesseractInfo Info()
    {
        lock (_gate)
        {
            if (_info is { Available: true } || (_info is not null && clock.GetUtcNow() - _probedAt < TimeSpan.FromSeconds(30))) return _info;
            _info = Probe();
            _probedAt = clock.GetUtcNow();
            return _info;
        }
    }

    private TesseractInfo Probe()
    {
        try
        {
            var (versionOut, versionErr) = Run(["--version"], null, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            var version = (versionOut + "\n" + versionErr).Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("tesseract ", StringComparison.OrdinalIgnoreCase))?["tesseract ".Length..].Trim();
            var (langsOut, _) = Run([.. TessdataArguments(), "--list-langs"], null, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            var installed = langsOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains(' ') && l != "osd").ToHashSet();

            string Pick(string wanted) => string.Join('+', wanted.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(installed.Contains));
            var text = Pick(Settings.Languages);
            var digits = Pick(Settings.OdometerLanguages);
            if (text.Length == 0)
                return new TesseractInfo(false, version, [.. installed.Order()], "", "", $"None of the languages '{Settings.Languages}' is installed.");
            return new TesseractInfo(true, version, [.. text.Split('+')], text, digits.Length > 0 ? digits : text, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException or IOException)
        {
            logger.LogWarning("Tesseract cannot be run ({Executable}): {Problem}", Settings.Executable, e.Message);
            return new TesseractInfo(false, null, [], "", "", e.Message);
        }
    }

    public IEnumerable<string> TessdataArguments() =>
        string.IsNullOrWhiteSpace(Settings.TessdataPath) ? [] : ["--tessdata-dir", Settings.TessdataPath];

    /// <summary>Runs Tesseract and returns standard output and error; throws when it fails or takes longer than <paramref name="timeout"/>.</summary>
    public async Task<(string Output, string Errors)> Run(IReadOnlyList<string> arguments, ReadOnlyMemory<byte>? input, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Settings.Executable)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["OMP_THREAD_LIMIT"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"'{Settings.Executable}' did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            if (input is { } bytes)
            {
                await process.StandardInput.BaseStream.WriteAsync(bytes, limit.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(limit.Token);
            var (stdout, stderr) = (await output, await errors);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"'{Settings.Executable}' exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {stderr.Trim()}");
            return (stdout, stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException($"'{Settings.Executable}' took longer than {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.");
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // it ended meanwhile
        }
    }
}

/// <summary>
/// One OCR pass with Tesseract. A pass that fails or times out reads as an empty page (and is logged): one bad pass must not lose the
/// whole reading, and the caller gets "unknown" rather than an error for a photo Tesseract chokes on.
/// </summary>
internal sealed class TesseractCliEngine(TesseractCli tesseract, IOptions<ReaderOptions> options, ILogger<TesseractCliEngine> logger) : IOcrEngine
{
    public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct)
    {
        var info = tesseract.Info();
        if (!info.Available) throw ReaderErrors.OcrUnavailable();

        var digits = pass.Purpose == OcrPurpose.Digits;
        List<string> arguments = ["stdin", "stdout", "-l", digits ? info.DigitLanguages : info.TextLanguages, "--psm", pass.PageSegmentation.ToString(CultureInfo.InvariantCulture)];
        arguments.AddRange(tesseract.TessdataArguments());
        if (digits) arguments.AddRange(["-c", "tessedit_char_whitelist=0123456789"]);
        arguments.Add("tsv");

        var started = Stopwatch.GetTimestamp();
        try
        {
            var (tsv, _) = await tesseract.Run(arguments, png, TimeSpan.FromSeconds(options.Value.Tesseract.TimeoutSeconds), ct);
            var page = TesseractTsv.Parse(tsv);
            logger.LogDebug("OCR pass {Variant}/psm {Psm}/{Purpose}: {Words} words in {Ms} ms", pass.Variant, pass.PageSegmentation, pass.Purpose, page.Words.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return page;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException)
        {
            logger.LogWarning("OCR pass {Variant}/psm {Psm}/{Purpose} failed: {Problem}", pass.Variant, pass.PageSegmentation, pass.Purpose, e.Message);
            return OcrPage.Empty;
        }
    }
}
