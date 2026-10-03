namespace Tankstat.Reader.Reading;

/// <summary>A failure the caller is told about: a stable code (the contract), a message for people, and the HTTP status.</summary>
public sealed class ReaderException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public static class ReaderErrors
{
    public static ReaderException BadRequest(string message) => new("bad_request", message, StatusCodes.Status400BadRequest);
    public static ReaderException BadImage() => new("bad_image", "The picture could not be decoded.", StatusCodes.Status400BadRequest);
    public static ReaderException Unauthorized() => new("unauthorized", "A valid X-Api-Key header is required.", StatusCodes.Status401Unauthorized);
    public static ReaderException TooLarge(int maxBytes) => new("too_large", $"The picture can be at most {maxBytes / (1024 * 1024)} MB.", StatusCodes.Status413PayloadTooLarge);
    public static ReaderException UnsupportedType() => new("unsupported_type", "Send a JPEG, PNG or WebP picture with its Content-Type.", StatusCodes.Status415UnsupportedMediaType);
    public static ReaderException Busy() => new("busy", "The reader is busy; try again in a moment.", StatusCodes.Status503ServiceUnavailable);
    public static ReaderException OcrUnavailable() => new("ocr_unavailable", "Tesseract cannot be run; see the health check.", StatusCodes.Status503ServiceUnavailable);
}
