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
}
