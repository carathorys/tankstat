namespace Tankstat.Domain.Images;

/// <summary>
/// Where an uploaded file lives below the data folder, so everything of a vehicle is in one place and goes away with it.
/// Built from ids only (never from user input): <c>users/&lt;id&gt;</c> and <c>vehicles/&lt;id&gt;/...</c>.
/// </summary>
public static class ImageFolders
{
    public static string Avatar(Guid userId) => $"users/{userId:N}";

    /// <summary>The folder of a vehicle: its picture and the photos of its logs are below it.</summary>
    public static string Vehicle(Guid vehicleId) => $"vehicles/{vehicleId:N}";

    public static string VehiclePicture(Guid vehicleId) => $"{Vehicle(vehicleId)}/picture";
}
