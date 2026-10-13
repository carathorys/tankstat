using System.IO.Compression;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Images;
using Tankstat.Application.Photos;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Images;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Backups;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>An instance with something in every corner (a user, a vehicle, logs, pictures, photos, a draft, a key) for the backup tests, and the readers of a backup file.</summary>
internal static class BackupSeed
{
    public static readonly DateOnly Day = new(2026, 9, 1);
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public const string Email = "zzcanary@example.com";
    public const string Name = "Zzcanary car";
    public const string Plate = "ZZ-CANARY-1";

    public sealed record Seeded(Guid Owner, Guid Vehicle, Guid Refueling, Guid Expense, Guid Picture, Guid Photo, Guid Draft);

    /// <summary>A user, a vehicle with a picture, a refuelling with a photo, an expense, and a draft; the files under the roots, a key in the keys folder.</summary>
    public static async Task<Seeded> SeedAsync(TestDatabase db, string pictures, string photosRoot, string keys)
    {
        var user = User.CreateLocal(Email, "Zz Canary", false);
        await db.Get<IUserRepository>().AddAsync(user, default);
        var vehicle = TestData.Vehicle(user.Id, Name, Plate);
        await db.Get<IVehicleRepository>().AddAsync(vehicle, default);
        var refueling = TestData.Refueling(user.Id, user.Id, vehicle.Id, Day, note: "zzcanary note");
        await db.Get<IRefuelingRepository>().AddAsync(refueling, default);
        var expense = Expense.Create(user.Id, user.Id, vehicle.Id, Day, "Tyres", null, Cost.Create(user.Id, vehicle.Id, Day, 100, "EUR"), OdometerReading.Create(user.Id, vehicle.Id, Day, 1500));
        await db.Get<IExpenseRepository>().AddAsync(expense, default);

        var images = db.Get<IImageRepository>();
        var picture = StoredImage.Create(Guid.NewGuid(), "image/webp", 3, Now, ImageFolders.VehiclePicture(vehicle.Id));
        var photo = StoredImage.Create(Guid.NewGuid(), "image/jpeg", 3, Now, ImageFolders.LogPhotos(vehicle.Id, LogType.Refueling, refueling.Id));
        var draft = StoredImage.Create(Guid.NewGuid(), "image/jpeg", 3, Now, ImageFolders.PhotoDrafts(vehicle.Id));
        var avatar = StoredImage.Create(Guid.NewGuid(), "image/webp", 3, Now, ImageFolders.Avatar(user.Id));
        foreach (var image in new[] { picture, photo, draft, avatar }) await images.AddAsync(image, default);
        await db.Get<ILogPhotoRepository>().AddAsync(LogPhoto.Create(user.Id, vehicle.Id, LogType.Refueling, refueling.Id, photo.Id, user.Id, Now), default);
        await db.Get<IPhotoDraftRepository>().AddAsync(PhotoDraft.Create(draft.Id, user.Id, vehicle.Id, user.Id, Now), default);

        WriteFile(Path.Combine(pictures, picture.Folder!, picture.Id.ToString("N")), "PIC");
        WriteFile(Path.Combine(pictures, avatar.Folder!, avatar.Id.ToString("N")), "AVA");
        WriteFile(Path.Combine(pictures, "legacy-file-id"), "OLD"); // a file from before folders existed
        WriteFile(Path.Combine(photosRoot, photo.Folder!, photo.Id.ToString("N")), "PHO");
        WriteFile(Path.Combine(photosRoot, draft.Folder!, draft.Id.ToString("N")), "DRA");
        WriteFile(Path.Combine(keys, "key-" + Guid.NewGuid().ToString("D") + ".xml"), "<key>zzsecret</key>");
        return new Seeded(user.Id, vehicle.Id, refueling.Id, expense.Id, picture.Id, photo.Id, draft.Id);
    }

    public static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public static string Manifest(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        using var reader = new StreamReader(zip.GetEntry(BackupManifest.FileName)!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>The inner archive, decrypted when an identity is given, extracted to a folder.</summary>
    public static string Inner(string file, byte[]? identity = null)
    {
        var folder = Directory.CreateTempSubdirectory("tankstat-inner-").FullName;
        using var zip = ZipFile.OpenRead(file);
        var entry = zip.GetEntry(identity is null ? BackupArchive.Plain : BackupArchive.Encrypted) ?? throw new InvalidOperationException("no inner archive");
        var innerPath = Path.Combine(folder, "inner.zip");
        using (var input = identity is null ? entry.Open() : AgeStream.Decrypt(entry.Open(), identity))
        using (var output = File.Create(innerPath))
            input.CopyTo(output);
        ZipFile.ExtractToDirectory(innerPath, folder);
        File.Delete(innerPath);
        return folder;
    }

    public static string[] Lines(string folder, string table) => File.ReadAllLines(Path.Combine(folder, "tables", table + ".jsonl"));

    public static IEnumerable<string> Files(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/')).Order();
}
