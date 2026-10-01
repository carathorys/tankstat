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

    public static void MapImportEndpoints(this WebApplication app)
    {
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
