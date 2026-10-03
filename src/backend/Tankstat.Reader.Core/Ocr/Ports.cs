namespace Tankstat.Reader.Core.Ocr;

/// <summary>
/// Ways a photo is offered to the OCR: as it is (in gray); inverted, for light digits on a dark display; and inverted with the strokes
/// thickened (two strengths), which closes the gaps between the segments of seven-segment digits so the OCR sees whole digits.
/// </summary>
public enum ImageVariant
{
    Normal,
    Inverted,
    Thickened,
    ThickenedMore,
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
