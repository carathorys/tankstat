using SkiaSharp;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Imaging;
using Tankstat.Reader.Reading;

namespace Tankstat.Reader.IntegrationTests;

public class PreparerTests
{
    private static readonly SkiaImagePreparer Preparer = new();

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void EverySupportedFormat_BecomesGrayPng(SKEncodedImageFormat format)
    {
        var image = Preparer.Prepare(TestImages.Make(1600, 1200, format));

        Assert.Equal((1600, 1200), (image.Width, image.Height));
        using var png = SKBitmap.Decode(image.Png(ImageVariant.Normal).ToArray());
        Assert.Equal((1600, 1200), (png.Width, png.Height));
        Assert.True(image.GrayAt(10, 10) > 240);   // white background
        Assert.True(image.GrayAt(800, 400) < 15);  // the black bar
    }

    [Fact]
    public void ASmallPhoto_IsEnlargedForTheOcr()
    {
        var image = Preparer.Prepare(TestImages.Make(800, 600));

        Assert.Equal((1600, 1200), (image.Width, image.Height));
    }

    [Fact]
    public void TheInvertedVariant_TurnsLightIntoDark()
    {
        var image = Preparer.Prepare(TestImages.Make(1600, 1200));

        using var inverted = SKBitmap.Decode(image.Png(ImageVariant.Inverted).ToArray());
        Assert.True(inverted.GetPixel(10, 10).Red < 15);
        Assert.True(inverted.GetPixel(800, 400).Red > 240);
    }

    [Fact]
    public void TransparencyIsShownOnWhite()
    {
        var image = Preparer.Prepare(TestImages.Make(1600, 1200, background: SKColors.Transparent));

        Assert.True(image.GrayAt(10, 10) > 240);
    }

    [Fact]
    public void WhatCannotBeDecoded_IsABadImage()
    {
        var error = Assert.Throws<ReaderException>(() => Preparer.Prepare("GIF89a not really"u8.ToArray()));

        Assert.Equal("bad_image", error.Code);
    }

    [Fact]
    public void TheThickenedVariants_CloseTheGapsBetweenSegments_OfLightDigitsOnADarkDisplay()
    {
        // Two light segments 4 px apart on a dark display, like the halves of a seven-segment digit.
        using var bitmap = new SKBitmap(1600, 1000);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var light = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(700, 400, 20, 100, light);
            canvas.DrawRect(700, 504, 20, 100, light);
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        var image = Preparer.Prepare(data.ToArray());

        var inverted = image.Pixels(ImageVariant.Inverted);
        var thickened = image.Pixels(ImageVariant.Thickened);
        var gap = 502 * image.Width + 710;

        Assert.True(inverted[gap] > 200);   // the gap is still there: light between two dark strokes
        Assert.True(thickened[gap] < 50);   // closed: one dark stroke
        Assert.Equal(3, image.ThickenRadius);
        Assert.True(image.Pixels(ImageVariant.ThickenedMore)[502 * image.Width + 694] < 50); // the stronger one grows further
    }
}
