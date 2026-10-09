using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Images;

namespace Tankstat.Infrastructure.UnitTests;

public class StorageOptionsTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "tankstat-options");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")] // Storage__PhotosPath= left blank in a compose file
    public void UnsetPhotosPath_KeepsThePhotosWithThePictures_SoNothingMoves(string? photos)
    {
        var options = new StorageOptions { Path = Path.Combine(Data, "uploads"), PhotosPath = photos };

        Assert.Equal(Path.Combine(Data, "uploads"), options.PhotosRoot);
        Assert.Equal((true, false), (options.OneRoot, options.RootsNested));
        Assert.True(new StorageOptions { Path = Path.GetPathRoot(Path.GetTempPath())! }.OneRoot); // also at the root of a disk
    }

    [Fact]
    public void BothRoots_AreFullPaths_WithoutATrailingSeparator()
    {
        var options = new StorageOptions { Path = "uploads/", PhotosPath = Path.Combine(Data, "photos") + Path.DirectorySeparatorChar };

        Assert.Equal(Path.GetFullPath("uploads"), options.PicturesRoot);
        Assert.Equal(Path.Combine(Data, "photos"), options.PhotosRoot);
    }

    [Theory]
    [InlineData("uploads", null, true, false)] // unset: with the pictures
    [InlineData("uploads", "uploads", true, false)] // one folder for both: allowed
    [InlineData("uploads", "uploads/", true, false)]
    [InlineData("uploads", "uploads/photos", false, true)]
    [InlineData("uploads/pictures", "uploads", false, true)]
    [InlineData("uploads", "uploads-photos", false, false)] // a common prefix is not one inside the other
    public void SameOrNested(string path, string? photos, bool oneRoot, bool nested)
    {
        var options = new StorageOptions { Path = Path.Combine(Data, path), PhotosPath = photos is null ? null : Path.Combine(Data, photos) };

        Assert.Equal((oneRoot, nested), (options.OneRoot, options.RootsNested));
    }

    [Fact]
    public void NestedRoots_AreRefusedAtStart_WithAMessageThatNamesBothSettings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Path"] = Path.Combine(Data, "uploads"),
            ["Storage:PhotosPath"] = Path.Combine(Data, "uploads", "photos"),
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddApplication(config).BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("Storage:PhotosPath", refused.Message);
        Assert.Contains("Storage:Path", refused.Message);
    }

    [Theory]
    [InlineData("")] // Storage__Path= in the environment, say a compose file's ${UPLOADS} left unset
    [InlineData("  ")]
    public void AnEmptyPicturesFolder_IsRefusedAtStart_WithAMessageThatNamesTheSetting(string path)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = path }).Build();
        using var services = new ServiceCollection().AddLogging().AddApplication(config).BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("Storage:Path", refused.Message);
    }
}
