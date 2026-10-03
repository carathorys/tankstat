using Tankstat.Application.Images;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Images;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.UnitTests;

public class PhotoReadingRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    /// <summary>A picture with a queued reading (readings belong to a picture).</summary>
    private static async Task<PhotoReading> Queue(TestDatabase db, DateTimeOffset? at = null, string folder = "vehicles/00000000000000000000000000000001/drafts")
    {
        var image = StoredImage.Create(Guid.NewGuid(), "image/webp", 10, Now, folder);
        await db.Get<IImageRepository>().AddAsync(image, default);
        var reading = PhotoReading.Queue(image.Id, ReadingPurpose.Refueling, "hu", 123_456, "HUF", Today, at ?? Now);
        await db.Get<IPhotoReadingRepository>().AddAsync(reading, default);
        return reading;
    }

    [Fact]
    public async Task AReadReading_RoundTrips_WithItsValues()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var reading = (await repo.ClaimAsync((await Queue(db)).Id, Now, default))!;

        reading.Complete("reader", "rules-1", DocumentKind.FuelReceipt,
            [new(ReadingFieldName.Total, "24669", 0.93, ValueSource.Read), new(ReadingFieldName.UnitPrice, "640.4", 0.5, ValueSource.Derived)], Now.AddSeconds(2));
        await repo.SaveAsync(reading, default);
        var loaded = Assert.Single(await repo.FindManyAsync([reading.Id, Guid.NewGuid()], default));

        Assert.Equal((ReadingStatus.Read, DocumentKind.FuelReceipt, "reader", "rules-1", 1, "hu", (long?)123_456, "HUF", Today),
            (loaded.Status, loaded.Kind, loaded.Provider, loaded.ModelVersion, loaded.Attempts, loaded.Locale, loaded.LastOdometer, loaded.Currency, loaded.Today));
        Assert.Equal(reading.Values, loaded.Values);
        Assert.Equal((Now.AddSeconds(2), Now), (loaded.ReadAt, loaded.ClaimedAt));
    }

    [Fact]
    public async Task OnlyDueReadingsAreListed_OldestFirst()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var later = await Queue(db, Now.AddMinutes(-1));
        var earlier = await Queue(db, Now.AddMinutes(-5));
        await Queue(db, Now.AddMinutes(1)); // not due yet
        var retried = (await repo.ClaimAsync((await Queue(db, Now.AddMinutes(-9))).Id, Now, default))!;
        retried.Retry(Now); // due again in a while

        await repo.SaveAsync(retried, default);

        Assert.Equal([earlier.Id, later.Id], await repo.ListDueAsync(Now, 10, default));
        Assert.Equal([earlier.Id], await repo.ListDueAsync(Now, 1, default));
        Assert.Contains(retried.Id, await repo.ListDueAsync(retried.DueAt, 10, default));
    }

    [Fact]
    public async Task AReadingIsClaimedOnce_EvenWhenTwoWorkersTryAtTheSameTime()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var reading = await Queue(db);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => repo.ClaimAsync(reading.Id, Now, default)));

        var claimed = Assert.Single(attempts, a => a is not null)!;
        Assert.Equal((ReadingStatus.Reading, 1, (DateTimeOffset?)Now), (claimed.Status, claimed.Attempts, claimed.ClaimedAt));
        Assert.Null(await repo.ClaimAsync(Guid.NewGuid(), Now, default));
        Assert.Null(await repo.ClaimAsync((await Queue(db, Now.AddMinutes(1))).Id, Now, default)); // not due yet
    }

    [Fact]
    public async Task ReadingsLeftBeingRead_AreFoundAsStale()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var old = (await repo.ClaimAsync((await Queue(db, Now.AddMinutes(-10))).Id, Now.AddMinutes(-10), default))!;
        await repo.ClaimAsync((await Queue(db)).Id, Now, default); // still being read
        await Queue(db); // queued, not claimed

        Assert.Equal([old.Id], (await repo.ListStaleAsync(Now.AddMinutes(-2), default)).Select(r => r.Id));
    }

    [Fact]
    public async Task AReadingGoesWithItsPicture_HoweverThePictureIsRemoved()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var images = db.Get<IImageRepository>();
        var single = await Queue(db);
        var inFolder = await Queue(db, folder: "vehicles/00000000000000000000000000000002/drafts");
        var kept = await Queue(db);

        await images.RemoveAsync(single.Id, default);
        await images.RemoveFolderAsync("vehicles/00000000000000000000000000000002", default);

        Assert.Equal([kept.Id], (await repo.FindManyAsync([single.Id, inFolder.Id, kept.Id], default)).Select(r => r.Id));
    }

    [Fact]
    public async Task SavingAReadingWhosePictureIsGone_DoesNothing_AndRemoveTakesIt()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IPhotoReadingRepository>();
        var gone = (await repo.ClaimAsync((await Queue(db)).Id, Now, default))!;
        var removed = await Queue(db);
        await db.Get<IImageRepository>().RemoveAsync(gone.Id, default);
        gone.Complete("reader", "rules-1", DocumentKind.Unknown, [], Now);

        await repo.SaveAsync(gone, default);
        await repo.RemoveAsync(removed.Id, default);

        Assert.Empty(await repo.FindManyAsync([gone.Id, removed.Id], default));
    }
}
