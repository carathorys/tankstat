using GreenDonut;
using Tankstat.Api.Media;
using Tankstat.Application.Photos;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

/// <summary>A photo of a log. <c>id</c> is the image's id (what removing it takes); <c>url</c> serves the picture. Uploading is plain HTTP (see <c>Api/Media</c>).</summary>
public sealed record LogPhotoInfo(Guid Id, string Url)
{
    public static LogPhotoInfo From(LogPhoto photo) => new(photo.ImageId, MediaUrls.Image(photo.ImageId)!);
}

/// <summary>The photos of a log by log id, one query for all logs of a response. Only used for logs that were already authorised.</summary>
public sealed class ExpensePhotosLoader(LogPhotoService photos, IBatchScheduler scheduler, DataLoaderOptions options)
    : GroupedDataLoader<Guid, LogPhoto>(scheduler, options)
{
    protected override async Task<ILookup<Guid, LogPhoto>> LoadGroupedBatchAsync(IReadOnlyList<Guid> keys, CancellationToken ct) =>
        await photos.ListForLogsAsync(LogType.Expense, keys, ct);
}

public sealed class RefuelingPhotosLoader(LogPhotoService photos, IBatchScheduler scheduler, DataLoaderOptions options)
    : GroupedDataLoader<Guid, LogPhoto>(scheduler, options)
{
    protected override async Task<ILookup<Guid, LogPhoto>> LoadGroupedBatchAsync(IReadOnlyList<Guid> keys, CancellationToken ct) =>
        await photos.ListForLogsAsync(LogType.Refueling, keys, ct);
}

[ExtendObjectType<Expense>]
public sealed class ExpensePhotoExtensions
{
    /// <summary>The expense's photos, oldest first.</summary>
    public async Task<IReadOnlyList<LogPhotoInfo>> GetPhotos([Parent] Expense expense, ExpensePhotosLoader loader, CancellationToken ct) =>
        expense.IsDeleted ? [] : (await loader.LoadAsync(expense.Id, ct) ?? []).Select(LogPhotoInfo.From).ToList();
}

[ExtendObjectType<Refueling>]
public sealed class RefuelingPhotoExtensions
{
    /// <summary>The log's photos, oldest first.</summary>
    public async Task<IReadOnlyList<LogPhotoInfo>> GetPhotos([Parent] Refueling refueling, RefuelingPhotosLoader loader, CancellationToken ct) =>
        refueling.IsDeleted ? [] : (await loader.LoadAsync(refueling.Id, ct) ?? []).Select(LogPhotoInfo.From).ToList();
}
