using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Photos;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

/// <summary>A draft photo of the current user with its reading (none when photo reading is off, or the photo was not queued).</summary>
public sealed record DraftReading(PhotoDraft Draft, PhotoReading? Reading);

/// <summary>
/// Photo reading for the user: photos picked in the add dialogs are queued for the recognition provider as soon as they are uploaded,
/// and the dialog asks for their readings until they are done. Everything is skipped when no provider is set up.
/// </summary>
public sealed class RecognitionService(
    RecognitionAvailability availability, RecognitionSetup setup, IPhotoReadingRepository readings, IPhotoDraftRepository drafts,
    RefuelingService refuelings, IOptions<VehicleDefaultsOptions> defaults, IRecognitionSignal signal, AccessService access, TimeProvider clock)
{
    private static readonly string[] Locales = ["hu", "en", "de"];

    /// <summary>A provider is set up and answers: the UI then waits for readings.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        await access.RequirePrincipalAsync(ct);
        return await availability.IsAvailableAsync(ct);
    }

    /// <summary>
    /// Queues the reading of a draft the current user just uploaded, with the hints the reader needs (the background worker that reads it
    /// has no user to ask). Nothing happens when no provider is set up, or when the draft is not the user's.
    /// </summary>
    /// <param name="locale">The language the user works in (how numbers and dates are written); unknown ones count as English.</param>
    public async Task QueueForDraftAsync(Guid draftId, ReadingPurpose purpose, string? locale, CancellationToken ct)
    {
        if (!availability.IsConfigured) return;
        var me = await access.RequirePrincipalAsync(ct);
        if (await drafts.FindAsync(draftId, ct) is not { } draft || draft.CreatedById != me.Id) return;

        var known = await refuelings.DefaultsAsync(draft.VehicleId, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await readings.AddAsync(
            PhotoReading.Queue(draft.Id, purpose, Language(locale), known?.LastOdometer, known?.LastCurrency ?? defaults.Value.Currency, today, clock.GetUtcNow()), ct);
        signal.Wake();
    }

    /// <summary>The current user's drafts among <paramref name="ids"/>, in that order, with their readings; anyone else's look missing.</summary>
    public async Task<IReadOnlyList<DraftReading>> ListDraftsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var now = clock.GetUtcNow();
        var wanted = ids.Distinct().Take(PhotoDraft.MaxPerUserAndVehicle).ToList();
        var own = (await drafts.FindManyAsync(wanted, ct)).Where(d => d.CreatedById == me.Id && !d.IsExpired(now)).ToDictionary(d => d.Id);
        var found = (await readings.FindManyAsync(own.Keys, ct)).ToDictionary(r => r.Id);
        return wanted.Where(own.ContainsKey).Select(id => new DraftReading(own[id], found.GetValueOrDefault(id))).ToList();
    }

    /// <summary>The values worth filling in: ones the provider is sure enough of, and read rather than the hint it was given coming back.</summary>
    public IReadOnlyList<ReadingValue> UsableValues(PhotoReading reading) =>
        reading.Values.Where(v => v.Source != ValueSource.Hint && v.Confidence >= setup.Options.MinConfidence).ToList();

    private static string Language(string? locale)
    {
        var language = (locale ?? "").Split('-', '_')[0].Trim().ToLowerInvariant();
        return Locales.Contains(language) ? language : "en";
    }
}
