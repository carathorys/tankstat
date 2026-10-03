using Microsoft.Extensions.Options;

namespace Tankstat.Application.Recognition;

/// <summary>Bound from the "Recognition" section (e.g. <c>Recognition__Provider=Reader</c>, <c>Recognition__Reader__BaseUrl=http://reader:8081</c>).</summary>
public sealed class RecognitionOptions
{
    public const string SectionName = "Recognition";

    /// <summary><c>None</c> (the default: photos are not read) or <c>Reader</c>, the optional photo reader service.</summary>
    public string? Provider { get; set; }

    public ReaderRecognitionOptions Reader { get; set; } = new();

    /// <summary>Values the provider is less sure of (0 to 1) are not filled in.</summary>
    public double MinConfidence { get; set; } = 0.6;

    /// <summary>Photos read at the same time.</summary>
    public int MaxConcurrent { get; set; } = 2;
}

public sealed class ReaderRecognitionOptions
{
    /// <summary>Where the reader listens, e.g. <c>http://reader:8081</c> (a path below it works too, behind a proxy).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The key the reader was started with (its <c>Reader__ApiKey</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one photo may take before the attempt counts as failed (and is tried again later).</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

public enum RecognitionProviderKind
{
    None,
    Reader,
}

/// <summary>
/// The Recognition settings, read once. Photo reading is optional, so settings that cannot be used (an unknown provider, a missing
/// address or key, a value that is not a number) never stop the app: they turn photo reading off, and the worker logs them as a warning.
/// </summary>
public sealed class RecognitionSetup
{
    public RecognitionSetup(IOptions<RecognitionOptions> options)
    {
        var problems = new List<string>();
        RecognitionOptions? bound = null;
        try
        {
            bound = options.Value;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or OptionsValidationException)
        {
            problems.Add($"The Recognition settings cannot be read ({e.Message}).");
        }
        Options = bound ?? new RecognitionOptions();
        Kind = bound is null ? RecognitionProviderKind.None : ParseKind(Options.Provider, problems);
        if (Kind != RecognitionProviderKind.None) Check(Options, problems);
        Problems = problems;
    }

    public RecognitionOptions Options { get; }
    public RecognitionProviderKind Kind { get; }

    /// <summary>Why the chosen provider cannot be used (empty when it can, or when none is chosen).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>A provider is chosen and its settings can be used.</summary>
    public bool Enabled => Kind != RecognitionProviderKind.None && Problems.Count == 0;

    /// <summary>Photo reading was asked for (or the settings could not even be read), so problems are worth a warning.</summary>
    public bool Requested => Kind != RecognitionProviderKind.None || Problems.Count > 0;

    private static RecognitionProviderKind ParseKind(string? provider, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(provider)) return RecognitionProviderKind.None;
        if (Enum.TryParse<RecognitionProviderKind>(provider.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind)) return kind;
        problems.Add($"Recognition:Provider '{provider}' is not known (use None or Reader).");
        return RecognitionProviderKind.None;
    }

    private static void Check(RecognitionOptions o, List<string> problems)
    {
        if (o.MinConfidence is < 0 or > 1) problems.Add("Recognition:MinConfidence must be between 0 and 1.");
        if (o.MaxConcurrent is < 1 or > 16) problems.Add("Recognition:MaxConcurrent must be between 1 and 16.");
        if (!Uri.TryCreate(o.Reader.BaseUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            problems.Add("Recognition:Reader:BaseUrl must be the reader's address, such as http://reader:8081.");
        if (string.IsNullOrWhiteSpace(o.Reader.ApiKey)) problems.Add("Recognition:Reader:ApiKey is required (the key the reader was started with, Reader__ApiKey).");
        if (o.Reader.TimeoutSeconds is < 1 or > 600) problems.Add("Recognition:Reader:TimeoutSeconds must be between 1 and 600.");
    }
}
