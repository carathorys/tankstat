using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tankstat.Reader.Core;
using Tankstat.Reader.Imaging;
using Tankstat.Reader.Ocr;
using Tankstat.Reader.Reading;

namespace Tankstat.Reader.Http;

public sealed record HealthResponse(string Status, string Version, string ModelVersion, OcrStatusInfo Ocr);
public sealed record OcrStatusInfo(string Engine, string? Version, IReadOnlyList<string> Languages);
public sealed record ErrorResponse(string Code, string Message);

/// <summary>
/// The reader's HTTP contract, v1: <c>GET /v1/health</c> (open, no secrets) and <c>POST /v1/read</c> (the photo as the raw body, hints
/// in the query, <c>X-Api-Key</c> required). Errors are <c>{code, message}</c> with a matching status; an unreadable photo is not an
/// error but a reading of kind "unknown".
/// </summary>
public static class ReaderEndpoints
{
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public static void MapReaderEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/health", (IOcrStatus ocr) =>
        {
            var info = ocr.Info();
            var body = new HealthResponse(info.Available ? "ok" : "unavailable", ReaderVersion.Value, DocumentReader.ModelVersion,
                new OcrStatusInfo("tesseract", info.Version, info.Languages));
            return Results.Json(body, ReaderJson.Options, statusCode: info.Available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        var v1 = app.MapGroup("/v1").AddEndpointFilter(TranslateErrors).AddEndpointFilter(RequireApiKey);
        v1.MapPost("/read", ReadAsync);
    }

    private static async Task<IResult> ReadAsync(
        HttpRequest request, DocumentReader reader, SkiaImagePreparer images, ReadGate gate, IOptions<ReaderOptions> options, TimeProvider clock,
        ILoggerFactory logs, CancellationToken ct)
    {
        var query = request.Query;
        var readRequest = new ReadRequest(Kinds(query["kinds"]), new ReadHints(
            ReadHints.NormalizeLocale(query["locale"]), LastOdometer(query["lastOdometer"]), Currency(query["currency"]), Today(query["today"], clock)));
        var contentType = request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (contentType is null || !ImageTypes.Contains(contentType)) throw ReaderErrors.UnsupportedType();
        var body = await ReadBodyAsync(request, options.Value.MaxImageBytes, ct);

        var started = Stopwatch.GetTimestamp();
        var result = await gate.RunAsync(token => reader.ReadAsync(images.Prepare(body), readRequest, token), ct);
        // Never the values: they are the user's data. What was read and how long it took is enough to run the service.
        logs.CreateLogger("Tankstat.Reader.Read").LogInformation("Read a {Kind} with {Fields} fields in {Ms:0} ms", result.Kind, result.Fields.Count,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Results.Json(result, ReaderJson.Options);
    }

    private static IReadOnlySet<string> Kinds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DocumentKinds.All.ToHashSet();
        var kinds = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var unknown = kinds.Where(k => !DocumentKinds.All.Contains(k)).ToList();
        if (unknown.Count > 0 || kinds.Count == 0)
            throw ReaderErrors.BadRequest($"Unknown kinds: {string.Join(", ", unknown)}. Use {string.Join(", ", DocumentKinds.All)}.");
        return kinds;
    }

    private static long? LastOdometer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw ReaderErrors.BadRequest("lastOdometer must be a whole number of at least 0.");
    }

    private static string? Currency(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var code = text.Trim().ToUpperInvariant();
        return code.Length == 3 && code.All(char.IsAsciiLetter) ? code : throw ReaderErrors.BadRequest("currency must be a three-letter ISO 4217 code.");
    }

    private static DateOnly Today(string? text, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(text)) return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var today)
            ? today
            : throw ReaderErrors.BadRequest("today must be a date like 2026-10-03.");
    }

    /// <summary>Reads the body, giving up as soon as it is larger than allowed (never buffers more than the limit).</summary>
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpRequest request, int limit, CancellationToken ct)
    {
        if (request.ContentLength > limit) throw ReaderErrors.TooLarge(limit);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit) throw ReaderErrors.TooLarge(limit);
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw ReaderErrors.BadImage();
        return buffer.ToArray();
    }

    private static ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<ReaderOptions>>().Value.ApiKey;
        var given = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
        // Hashing first makes the comparison take the same time whatever the lengths.
        var same = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        if (!same || expected.Length == 0) throw ReaderErrors.Unauthorized();
        return next(context);
    }

    private static async ValueTask<object?> TranslateErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ReaderException e)
        {
            if (e.Code == "busy") context.HttpContext.Response.Headers.RetryAfter = "2";
            return Results.Json(new ErrorResponse(e.Code, e.Message), ReaderJson.Options, statusCode: e.Status);
        }
    }
}
