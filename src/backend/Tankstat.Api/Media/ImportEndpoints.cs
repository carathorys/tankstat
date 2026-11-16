using Tankstat.Application.Imports;
using Tankstat.Domain;

namespace Tankstat.Api.Media;

/// <summary>
/// A file to import is uploaded as the raw request body (like pictures: not GraphQL). The answer is a token; what the file contains and the
/// confirmation go through GraphQL (<c>importPreview</c>, <c>confirmImport</c>). Nothing is saved by the upload.
/// </summary>
public static class ImportEndpoints
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>The source's name for a Tankstat backup or export: a route of its own, since its file is far larger than a CSV.</summary>
    public const string BackupFormat = "tankstat";

    public static void MapImportEndpoints(this WebApplication app)
    {
        // A backup or an export to import cars from (#336): streamed to the server's scratch folder, never held in memory. Who is asking,
        // the switch and the announced size are checked before a byte of the body is read; Kestrel's own limit is lifted only then, and
        // every byte is counted against ours. The answer is the file's session; how the opening stands is asked through GraphQL.
        app.MapPost("/imports/" + BackupFormat, async (HttpRequest request, BackupImportService imports, CancellationToken ct) =>
        {
            var uploader = await imports.RequireUploaderAsync(request.ContentLength, ct);
            if (request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = null;
            var status = await imports.UploadAsync(uploader, request.Body, ct);
            return Results.Json(new { token = status.Token, format = BackupFormat });
        }).AddEndpointFilter(MediaEndpoints.TranslateErrors);

        app.MapPost("/imports/{format}", async (string format, HttpRequest request, ImportService imports, CancellationToken ct) =>
        {
            if (request.ContentLength > MaxBytes) throw TooLarge();

            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes) throw TooLarge();
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;

            var upload = await imports.UploadAsync(format, buffer, ct);
            return Results.Json(new { token = upload.Token, format = upload.Format });
        }).AddEndpointFilter(MediaEndpoints.TranslateErrors);
    }

    private static DomainException TooLarge() =>
        new("import.tooLarge", $"The file can be at most {MaxBytes / (1024 * 1024)} MB.", new { MaxMb = MaxBytes / (1024 * 1024) });
}
