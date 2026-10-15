using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Seeder.Pictures;

public sealed record PicturesResult(int Photos, int Drawn);

/// <summary>
/// Gives every seeded vehicle (the trash's too) a picture, stored as the app stores an uploaded one: the file through <see cref="IImageStore"/>
/// into the vehicle's picture folder, its <see cref="StoredImage"/> row, and <c>Vehicle.SetPicture</c>. Random but deterministic: each vehicle
/// has a <see cref="Random"/> of its own from the seed and its name (<see cref="RandomFor"/>), so its picture depends on nothing else and the
/// rest of a seed stays as it was. With <paramref name="photos"/> it picks one of its model's candidates (vehicles of one model differ), else,
/// or when that photo cannot be had, a drawn one.
/// </summary>
public sealed partial class VehiclePictures(IDbContextFactory<AppDbContext> dbFactory, IImageStore store, TimeProvider clock, CommonsPictures? photos)
{
    private const int Batch = 200;

    public async Task<PicturesResult> AddAsync(int seed, CancellationToken ct)
    {
        var candidatesOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int found = 0, drawn = 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var vehicles = await db.Vehicles.IgnoreQueryFilters().OrderBy(v => v.Name).ToListAsync(ct);
        foreach (var batch in vehicles.Chunk(Batch))
        {
            foreach (var vehicle in batch)
            {
                var random = RandomFor(seed, vehicle.Name);
                byte[]? photo = null;
                if (photos is not null)
                {
                    var model = ModelOf(vehicle.Name);
                    if (!candidatesOf.TryGetValue(model, out var candidates)) candidatesOf[model] = candidates = await photos.CandidatesAsync(model, ct);
                    if (candidates.Count > 0) photo = await photos.PhotoAsync(candidates[random.Next(candidates.Count)], ct);
                }
                var bytes = photo ?? DrawnPicture.Png(random);
                if (photo is null) drawn++;
                else found++;

                var image = StoredImage.Create(Guid.NewGuid(), ImageFormat.Detect(bytes), bytes.Length, clock.GetUtcNow(), ImageFolders.VehiclePicture(vehicle.Id));
                await store.SaveAsync(image, bytes, ct);
                db.Images.Add(image);
                vehicle.SetPicture(image.Id);
            }
            await db.SaveChangesAsync(ct);
        }
        return new PicturesResult(found, drawn);
    }

    /// <summary>
    /// A vehicle's own <see cref="Random"/>: from the seed and its name (unique in a seed), through FNV-1a, since <c>string.GetHashCode</c>
    /// differs from one process to the next.
    /// </summary>
    public static Random RandomFor(int seed, string name)
    {
        var hash = 2166136261u ^ (uint)seed;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(name)) hash = (hash ^ b) * 16777619u;
        return new Random(unchecked((int)hash));
    }

    /// <summary>The model a seeded vehicle's name starts with ("Skoda Octavia (family) 2" is "Skoda Octavia").</summary>
    public static string ModelOf(string name) => Model().Match(name) is { Success: true } m ? m.Groups[1].Value : name;

    [GeneratedRegex(@"^(.+?) \(")]
    private static partial Regex Model();
}
