namespace Tankstat.Reader.Core.Ocr;

/// <summary>Ways a photo is offered to the OCR: as it is (in gray), or inverted for light digits on a dark display.</summary>
public enum ImageVariant
{
    Normal,
    Inverted,
}

/// <summary>A photo the host decoded and prepared for the OCR, available as PNG in each <see cref="ImageVariant"/>.</summary>
public interface IPreparedImage
{
    int Width { get; }
    int Height { get; }
    ReadOnlyMemory<byte> Png(ImageVariant variant);
}

/// <summary>What a pass looks for: any text (receipt languages), or digits only (the odometer).</summary>
public enum OcrPurpose
{
    Text,
    Digits,
}

/// <param name="PageSegmentation">Tesseract's page segmentation mode: 6 = one block of text, 7 = one line, 11 = sparse text anywhere.</param>
public sealed record OcrPass(ImageVariant Variant, int PageSegmentation, OcrPurpose Purpose);

/// <summary>Reads the words of a PNG picture. The host implements it with Tesseract; tests fake it.</summary>
public interface IOcrEngine
{
    Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct);
}
