using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Images;
using Tankstat.Application.Photos;
using Tankstat.Domain;
using Tankstat.Domain.Photos;

namespace Tankstat.Api.Media;

/// <summary>
/// Binary pictures cannot travel through GraphQL, so uploads and downloads are plain HTTP (the one exception to "GraphQL only").
/// They use the same session cookie and the same access rules as everything else. An upload is the raw image as the request body
/// of a PUT or DELETE: a browser cannot send those cross-site without a CORS preflight, and no CORS policy is configured (adding
/// one would need the uploads protected separately).
/// </summary>
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var media = app.MapGroup("/media").AddEndpointFilter(TranslateErrors);

        media.MapGet("/{id:guid}", async (Guid id, ImageService images, HttpContext http, CancellationToken ct) =>
        {
            var image = await images.OpenAsync(id, ct);
            if (image is null) return Results.NotFound();

            var headers = http.Response.Headers;
            headers.CacheControl = "private, max-age=31536000, immutable";
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            headers.ContentDisposition = "inline";
            return Results.Stream(image.Content, image.ContentType);
        });

        media.MapPut("/me/avatar", async (HttpRequest request, ImageService images, CancellationToken ct) =>
            Uploaded(await images.SetAvatarAsync(await ReadBodyAsync(request, ct), ct)));
        media.MapDelete("/me/avatar", async (ImageService images, CancellationToken ct) =>
        {
            await images.RemoveAvatarAsync(ct);
            return Results.NoContent();
        });

        media.MapPut("/vehicles/{vehicleId:guid}/picture", async (Guid vehicleId, HttpRequest request, ImageService images, CancellationToken ct) =>
            Uploaded(await images.SetVehiclePictureAsync(vehicleId, await ReadBodyAsync(request, ct), ct)));
        media.MapDelete("/vehicles/{vehicleId:guid}/picture", async (Guid vehicleId, ImageService images, CancellationToken ct) =>
        {
            await images.RemoveVehiclePictureAsync(vehicleId, ct);
            return Results.NoContent();
        });

        // Photos of logs: PUT adds one (up to ten per log), DELETE removes one by its image id.
        foreach (var (segment, logType) in new[] { ("expenses", LogType.Expense), ("refuelings", LogType.Refueling) })
        {
            media.MapPut($"/{segment}/{{logId:guid}}/photos", async (Guid logId, HttpRequest request, LogPhotoService photos, CancellationToken ct) =>
                Uploaded(await photos.AddAsync(logType, logId, await ReadBodyAsync(request, ct), ct)));
            media.MapDelete($"/{segment}/{{logId:guid}}/photos/{{imageId:guid}}", async (Guid logId, Guid imageId, LogPhotoService photos, CancellationToken ct) =>
            {
                await photos.RemoveAsync(logType, logId, imageId, ct);
                return Results.NoContent();
            });
        }
    }

    private static IResult Uploaded(Guid id) => Results.Json(new { id, url = MediaUrls.Image(id) });

    /// <summary>Reads the body, giving up as soon as it is larger than a picture may be (never buffers more than the limit).</summary>
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        var limit = ImageFormat.MaxBytes;
        if (request.ContentLength > limit) throw TooLarge();

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit) throw TooLarge();
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static DomainException TooLarge() =>
        new("image.tooLarge", $"The picture can be at most {ImageFormat.MaxBytes / 1024} KB.", new { MaxKb = ImageFormat.MaxBytes / 1024 });

    /// <summary>The same stable keys as in GraphQL errors, as JSON with a matching HTTP status.</summary>
    internal static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (KeyedException e)
        {
            var status = e switch
            {
                UnauthenticatedException => StatusCodes.Status401Unauthorized,
                ForbiddenException => StatusCodes.Status403Forbidden,
                NotFoundException => StatusCodes.Status404NotFound,
                _ when e.Key.EndsWith(".tooLarge", StringComparison.Ordinal) => StatusCodes.Status413PayloadTooLarge,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Json(new { key = e.Key, args = e.Args, message = e.Message }, statusCode: status);
        }
    }
}
