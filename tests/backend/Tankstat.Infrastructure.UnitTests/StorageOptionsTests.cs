using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Images;

namespace Tankstat.Infrastructure.UnitTests;

public class StorageOptionsTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "tankstat-options");

    [Fact]
    public void UnsetPhotosPath_IsPhotosNextToThePicturesFolder_WhereverThatIs()
    {
        Assert.Equal(Path.Combine(Data, "photos"), new StorageOptions { Path = Path.Combine(Data, "uploads") }.PhotosRoot);
        Assert.Equal(Path.Combine(Data, "photos"), new StorageOptions { Path = Path.Combine(Data, "uploads") + Path.DirectorySeparatorChar }.PhotosRoot); // never inside it
        Assert.Equal(Path.GetFullPath("photos"), new StorageOptions().PhotosRoot); // the defaults: uploads and photos side by side
    }

    [Fact]
    public void BothRoots_AreFullPaths_WithoutATrailingSeparator()
    {
        var options = new StorageOptions { Path = "uploads/", PhotosPath = Path.Combine(Data, "photos") + Path.DirectorySeparatorChar };

        Assert.Equal(Path.GetFullPath("uploads"), options.PicturesRoot);
        Assert.Equal(Path.Combine(Data, "photos"), options.PhotosRoot);
    }

    [Theory]
    [InlineData("uploads", null, false, false)]
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
}
