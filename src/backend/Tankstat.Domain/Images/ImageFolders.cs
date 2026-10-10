using Tankstat.Domain.Photos;

namespace Tankstat.Domain.Images;

/// <summary>
/// Where an uploaded file lives below its root, so everything of a vehicle is in one place and goes away with it.
/// Built from ids only (never from user input): <c>users/&lt;id&gt;</c> and <c>vehicles/&lt;id&gt;/...</c>. Pictures and the photos of logs
/// have roots of their own (<see cref="RootsOf"/>); the layout below each root is the same.
/// </summary>
public static class ImageFolders
{
    private const string Picture = "picture";
    private const string Drafts = "drafts";
    private const string Expenses = "expenses";
    private const string Refuelings = "refuelings";

    public static string Avatar(Guid userId) => $"users/{userId:N}";

    /// <summary>The folder of a vehicle: its picture and the photos of its logs are below it.</summary>
    public static string Vehicle(Guid vehicleId) => $"vehicles/{vehicleId:N}";

    public static string VehiclePicture(Guid vehicleId) => $"{Vehicle(vehicleId)}/{Picture}";

    /// <summary>Photos uploaded for logs that are not saved yet (see <c>PhotoDraft</c>); saving the log moves them to its folder.</summary>
    public static string PhotoDrafts(Guid vehicleId) => $"{Vehicle(vehicleId)}/{Drafts}";

    /// <summary>The photos of one log: <c>vehicles/&lt;id&gt;/expenses/&lt;logId&gt;</c> or <c>.../refuelings/&lt;logId&gt;</c>.</summary>
    public static string LogPhotos(Guid vehicleId, LogType logType, Guid logId) =>
        $"{Vehicle(vehicleId)}/{(logType == LogType.Expense ? Expenses : Refuelings)}/{logId:N}";

    /// <summary>The folders right below a vehicle's folder that hold photos of logs; the rest of it is the vehicle's picture.</summary>
    public static IReadOnlyList<string> PhotoKinds { get; } = [Drafts, Refuelings, Expenses];

    /// <summary>
    /// Which root a folder lives under: the pictures (avatars, vehicle pictures and the files from before folders, which have none) or the
    /// photos of logs (their drafts and the photos of refuellings and expenses), which grow without limit and may live on a disk of their own.
    /// A vehicle's own folder is under both: its picture in one, its photos in the other.
    /// </summary>
    public static ImageRoots RootsOf(string? folder)
    {
        if (folder is null || !folder.StartsWith("vehicles/", StringComparison.Ordinal)) return ImageRoots.Pictures;
        var parts = folder.Split('/');
        if (parts.Length == 2) return ImageRoots.Pictures | ImageRoots.Photos;
        return PhotoKinds.Contains(parts[2]) ? ImageRoots.Photos : ImageRoots.Pictures;
    }
}
