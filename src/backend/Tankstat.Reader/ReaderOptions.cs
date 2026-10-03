namespace Tankstat.Reader;

/// <summary>
/// The reader's settings (section <c>Reader</c>; environment variables <c>Reader__ApiKey</c>, <c>Reader__Tesseract__Languages</c>, ...).
/// Only the API key is required: the reader is reached by the Tankstat app over the network and must not answer anybody else.
/// </summary>
public sealed class ReaderOptions
{
    public const string SectionName = "Reader";

    /// <summary>The shared secret callers send in the <c>X-Api-Key</c> header. The reader refuses to start without one.</summary>
    public string ApiKey { get; set; } = "";

    public TesseractOptions Tesseract { get; set; } = new();

    /// <summary>Photos read at the same time (each runs Tesseract a few times, one core each).</summary>
    public int MaxConcurrent { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>Photos that may wait for a free slot; beyond that the reader answers 503 busy.</summary>
    public int QueueLimit { get; set; } = 8;

    public int MaxImageBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Where the reader keeps what it learns (training examples and models, in later versions).</summary>
    public string DataPath { get; set; } = "data";
}

public sealed class TesseractOptions
{
    public string Executable { get; set; } = "tesseract";

    /// <summary>Languages for receipts, in Tesseract's form; the ones that are not installed are left out (the health check lists the rest).</summary>
    public string Languages { get; set; } = "hun+eng+deu";

    /// <summary>Languages for the digit passes of odometers; another traineddata (e.g. one made for seven-segment displays) can go here.</summary>
    public string OdometerLanguages { get; set; } = "eng";

    /// <summary>A folder of extra or better traineddata files (tessdata_best, ...); the installed ones when empty.</summary>
    public string? TessdataPath { get; set; }

    public int TimeoutSeconds { get; set; } = 20;
}
