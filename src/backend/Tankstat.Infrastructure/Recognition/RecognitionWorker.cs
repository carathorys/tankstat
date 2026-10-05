using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>Wakes the worker as soon as a photo is queued; without it the worker still looks every <see cref="RecognitionWorker.Sweep"/>.</summary>
internal sealed class RecognitionSignal : IRecognitionSignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already woken
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _wake.WaitAsync(timeout, ct);
}

/// <summary>
/// Reads queued photos in the background (the app's only long-running job). It is always registered but returns at once when photo
/// reading is not set up; settings that cannot be used are logged as a warning, never thrown. Queued readings live in the database,
/// so they survive a restart, and the periodic look also picks up retries and attempts cut short.
/// </summary>
internal sealed class RecognitionWorker(IServiceScopeFactory scopes, RecognitionSetup setup, RecognitionSignal signal, ILogger<RecognitionWorker> logger)
    : BackgroundService
{
    internal static readonly TimeSpan Sweep = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!setup.Enabled)
        {
            if (setup.Requested) logger.LogWarning("Photo reading is turned off because its settings cannot be used: {Problems}", string.Join(" ", setup.Problems));
            return;
        }
        var model = setup.Options.OpenAiCompatible;
        logger.LogInformation("Photo reading uses the {Provider} provider: model {Model} at {Server}", setup.Kind, model.Model, ServerAddress.Of(model.BaseUrl));
        logger.LogInformation("System prompt: {PromptSource} ({Characters} characters)",
            setup.SystemPromptSetting is { } named ? $"from {named}" : "built-in", (setup.SystemPrompt ?? OpenAiCompatiblePrompt.DefaultSystemPrompt).Length);

        while (!stopping.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                foreach (var done in await scope.ServiceProvider.GetRequiredService<PhotoReadingProcessor>().ProcessDueAsync(stopping))
                {
                    worked = true;
                    // Never the values: only which photo, what happened and why, and in counts what came of a read.
                    if (done is { Outcome: ReadingOutcome.Read, Summary: { } read })
                        logger.LogDebug("Read photo {Id}: it shows {Kind}, {Sure} of {Values} values are sure enough to be filled in (rated {MinConfidence} or more)",
                            done.Id, read.Kind, read.Sure, read.Values, setup.Options.MinConfidence);
                    else logger.LogInformation("Photo {Id}: {Outcome} ({Reason})", done.Id, done.Outcome, done.Reason);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Reading photos failed; trying again in {Seconds} seconds", Sweep.TotalSeconds);
            }

            if (worked) continue; // more may be due right away
            try
            {
                await signal.WaitAsync(Sweep, stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
