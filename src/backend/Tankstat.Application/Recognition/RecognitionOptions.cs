using Microsoft.Extensions.Options;

namespace Tankstat.Application.Recognition;

/// <summary>
/// Bound from the "Recognition" section (e.g. <c>Recognition__Provider=Reader</c>, <c>Recognition__Reader__BaseUrl=http://reader:8081</c>,
/// or <c>Recognition__Provider=OpenAiCompatible</c> with <c>Recognition__OpenAiCompatible__BaseUrl=http://localhost:1234/v1</c>).
/// </summary>
public sealed class RecognitionOptions
{
    public const string SectionName = "Recognition";

    /// <summary>
    /// <c>None</c> (the default: photos are not read), <c>Reader</c> (the optional photo reader service) or <c>OpenAiCompatible</c> (a vision
    /// model behind an OpenAI-compatible API: a local server such as LM Studio, Ollama or llama.cpp, or a paid one).
    /// </summary>
    public string? Provider { get; set; }

    public ReaderRecognitionOptions Reader { get; set; } = new();

    public OpenAiCompatibleRecognitionOptions OpenAiCompatible { get; set; } = new();

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

/// <summary>
/// A vision model behind an OpenAI-compatible chat completions API (<c>POST {BaseUrl}/chat/completions</c>, <c>GET {BaseUrl}/models</c>): a
/// server the operator runs (LM Studio, Ollama, llama.cpp, vLLM) or a paid API, which then gets every photo.
/// </summary>
public sealed class OpenAiCompatibleRecognitionOptions
{
    /// <summary>The API's address including its version, e.g. <c>http://localhost:1234/v1</c> or <c>https://api.openai.com/v1</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sent as a bearer token when set; servers on the operator's own network usually need none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model's name as the server lists it (<c>GET /v1/models</c>).</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Replaces the built-in system prompt (the guidance on how to read a dashboard or a receipt). What the model has to answer, and in which
    /// shape, is always added by the app.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>A file holding the system prompt, for long ones (read once, at startup); it wins over <see cref="SystemPrompt"/>.</summary>
    public string? SystemPromptFile { get; set; }

    /// <summary>How the shape of the answer is enforced: <c>JsonSchema</c> (most servers), <c>JsonObject</c> (older ones) or <c>None</c>.</summary>
    public OpenAiResponseFormat ResponseFormat { get; set; } = OpenAiResponseFormat.JsonSchema;

    /// <summary>Sent only when set (0 to 2): <c>0</c> makes a local model read the same digits the same way every time; some paid models refuse it.</summary>
    public double? Temperature { get; set; }

    /// <summary>How long one photo may take before the attempt counts as failed (and is tried again later); a model on a CPU is slow.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

public enum OpenAiResponseFormat
{
    /// <summary><c>response_format: json_schema</c> with the exact shape of the answer (OpenAI, LM Studio, Ollama, llama.cpp, vLLM).</summary>
    JsonSchema,
    /// <summary><c>response_format: json_object</c>: any JSON object (servers without schema support).</summary>
    JsonObject,
    /// <summary>Nothing asked for beyond the prompt: the JSON is picked out of the text.</summary>
    None,
}

public enum RecognitionProviderKind
{
    None,
    Reader,
    OpenAiCompatible,
}

/// <summary>
/// The Recognition settings, read once. Photo reading is optional, so settings that cannot be used (an unknown provider, a missing
/// address or key, a value that is not a number) never stop the app: they turn photo reading off, and the worker logs them as a warning.
/// </summary>
public sealed class RecognitionSetup
{
    /// <summary>A prompt file larger than this is the wrong file, not a prompt.</summary>
    public const int MaxPromptFileBytes = 16 * 1024;

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
        if (Kind != RecognitionProviderKind.None) Check(Kind, Options, problems);
        if (Kind == RecognitionProviderKind.OpenAiCompatible) SystemPrompt = OwnPrompt(Options.OpenAiCompatible, problems);
        Problems = problems;
    }

    public RecognitionOptions Options { get; }
    public RecognitionProviderKind Kind { get; }

    /// <summary>Why the chosen provider cannot be used (empty when it can, or when none is chosen).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The operator's own system prompt for a model (from the file, else the setting); null for the built-in one.</summary>
    public string? SystemPrompt { get; }

    /// <summary>How long one read may take with the chosen provider (its <c>TimeoutSeconds</c>): what a read that was cut short is measured against.</summary>
    public TimeSpan ReadTimeout =>
        TimeSpan.FromSeconds(Kind == RecognitionProviderKind.OpenAiCompatible ? Options.OpenAiCompatible.TimeoutSeconds : Options.Reader.TimeoutSeconds);

    /// <summary>A provider is chosen and its settings can be used.</summary>
    public bool Enabled => Kind != RecognitionProviderKind.None && Problems.Count == 0;

    /// <summary>Photo reading was asked for (or the settings could not even be read), so problems are worth a warning.</summary>
    public bool Requested => Kind != RecognitionProviderKind.None || Problems.Count > 0;

    private static RecognitionProviderKind ParseKind(string? provider, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(provider)) return RecognitionProviderKind.None;
        if (Enum.TryParse<RecognitionProviderKind>(provider.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind)) return kind;
        problems.Add($"Recognition:Provider '{provider}' is not known (use None, Reader or OpenAiCompatible).");
        return RecognitionProviderKind.None;
    }

    private static void Check(RecognitionProviderKind kind, RecognitionOptions o, List<string> problems)
    {
        if (o.MinConfidence is < 0 or > 1) problems.Add("Recognition:MinConfidence must be between 0 and 1.");
        if (o.MaxConcurrent is < 1 or > 16) problems.Add("Recognition:MaxConcurrent must be between 1 and 16.");
        switch (kind)
        {
            case RecognitionProviderKind.Reader:
                if (!IsHttpUrl(o.Reader.BaseUrl)) problems.Add("Recognition:Reader:BaseUrl must be the reader's address, such as http://reader:8081.");
                if (string.IsNullOrWhiteSpace(o.Reader.ApiKey)) problems.Add("Recognition:Reader:ApiKey is required (the key the reader was started with, Reader__ApiKey).");
                if (o.Reader.TimeoutSeconds is < 1 or > 600) problems.Add("Recognition:Reader:TimeoutSeconds must be between 1 and 600.");
                break;
            case RecognitionProviderKind.OpenAiCompatible:
                var m = o.OpenAiCompatible;
                if (!IsHttpUrl(m.BaseUrl)) problems.Add("Recognition:OpenAiCompatible:BaseUrl must be the API's address including its version, such as http://localhost:1234/v1.");
                if (string.IsNullOrWhiteSpace(m.Model)) problems.Add("Recognition:OpenAiCompatible:Model is required (the model's name as the server lists it).");
                if (m.TimeoutSeconds is < 1 or > 600) problems.Add("Recognition:OpenAiCompatible:TimeoutSeconds must be between 1 and 600.");
                if (m.Temperature is < 0 or > 2) problems.Add("Recognition:OpenAiCompatible:Temperature must be between 0 and 2.");
                break;
        }
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>The operator's prompt: the file when one is named (a missing, empty or oversized one is a problem), else the setting, else none.</summary>
    private static string? OwnPrompt(OpenAiCompatibleRecognitionOptions o, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(o.SystemPromptFile)) return string.IsNullOrWhiteSpace(o.SystemPrompt) ? null : o.SystemPrompt.Trim();
        try
        {
            var file = new FileInfo(o.SystemPromptFile);
            if (!file.Exists)
            {
                problems.Add("Recognition:OpenAiCompatible:SystemPromptFile does not exist (an absolute path, such as /data/prompt.txt, is safest).");
                return null;
            }
            if (file.Length > MaxPromptFileBytes)
            {
                problems.Add($"Recognition:OpenAiCompatible:SystemPromptFile is larger than {MaxPromptFileBytes / 1024} KB: that is not a prompt.");
                return null;
            }
            var text = File.ReadAllText(file.FullName).Trim();
            if (text.Length > 0) return text;
            problems.Add("Recognition:OpenAiCompatible:SystemPromptFile is empty.");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            problems.Add("Recognition:OpenAiCompatible:SystemPromptFile cannot be read."); // never its content, nor the system's words about it
            return null;
        }
    }
}
