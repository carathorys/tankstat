namespace Tankstat.Domain.Images;

/// <summary>Metadata of an uploaded picture; the bytes live in the file store under the same id. Replacing a picture creates a new image.</summary>
public sealed class StoredImage
{
    private StoredImage() { } // EF Core

    public Guid Id { get; private set; }
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StoredImage Create(Guid id, string contentType, long sizeBytes, DateTimeOffset now) =>
        new() { Id = id, ContentType = contentType, SizeBytes = sizeBytes, CreatedAt = now };
}
