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

[ExtendObjectType<Expense>]
public sealed class ExpensePhotoExtensions
{
    /// <summary>The expense's photos, oldest first.</summary>
    public async Task<IReadOnlyList<LogPhotoInfo>> GetPhotos([Parent] Expense expense, [Service] LogPhotoService photos, CancellationToken ct) =>
        (await photos.ListAsync(LogType.Expense, expense.Id, ct)).Select(LogPhotoInfo.From).ToList();
}

[ExtendObjectType<Refueling>]
public sealed class RefuelingPhotoExtensions
{
    /// <summary>The log's photos, oldest first.</summary>
    public async Task<IReadOnlyList<LogPhotoInfo>> GetPhotos([Parent] Refueling refueling, [Service] LogPhotoService photos, CancellationToken ct) =>
        (await photos.ListAsync(LogType.Refueling, refueling.Id, ct)).Select(LogPhotoInfo.From).ToList();
}
