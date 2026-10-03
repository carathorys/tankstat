using Microsoft.Extensions.DependencyInjection.Extensions;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;
using Tankstat.Reader.Imaging;
using Tankstat.Reader.Ocr;
using Tankstat.Reader.Reading;

namespace Tankstat.Reader;

public static class ReaderServices
{
    /// <param name="requireApiKey">False for the command line tools (eval, synth), which never answer requests.</param>
    public static IServiceCollection AddReader(this IServiceCollection services, IConfiguration configuration, bool requireApiKey = true)
    {
        var options = services.AddOptions<ReaderOptions>()
            .Bind(configuration.GetSection(ReaderOptions.SectionName))
            .Validate(o => !requireApiKey || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Reader:ApiKey is required (environment variable Reader__ApiKey); callers send it in the X-Api-Key header.")
            .Validate(o => o.MaxConcurrent >= 1, "Reader:MaxConcurrent must be at least 1.")
            .Validate(o => o.QueueLimit >= 0, "Reader:QueueLimit cannot be negative.")
            .Validate(o => o.MaxImageBytes > 0, "Reader:MaxImageBytes must be positive.")
            .Validate(o => o.Tesseract.TimeoutSeconds >= 1, "Reader:Tesseract:TimeoutSeconds must be at least 1.");
        if (requireApiKey) options.ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TesseractCli>();
        services.AddSingleton<IOcrStatus>(sp => sp.GetRequiredService<TesseractCli>());
        services.AddSingleton<IOcrEngine, TesseractCliEngine>();
        services.AddSingleton(Lexicon.Default);
        services.AddSingleton<ICandidateScorer, RuleScorer>();
        services.AddSingleton(sp => new DocumentReader(sp.GetRequiredService<IOcrEngine>(), sp.GetRequiredService<Lexicon>(), sp.GetRequiredService<ICandidateScorer>()));
        services.AddSingleton<SkiaImagePreparer>();
        services.AddSingleton<ReadGate>();
        return services;
    }
}
