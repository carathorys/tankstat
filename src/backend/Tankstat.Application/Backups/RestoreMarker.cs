using System.Text.Json;

namespace Tankstat.Application.Backups;

/// <summary>
/// A restore that waits for the next start: <c>restore.pending</c> in the backups folder names the backup (a file name in that folder, never
/// a path), who asked and when, and carries the identity that opens an encrypted backup (in plain, which is why the file is readable by the
/// app's user only and goes with the restart). The start-up step applies it and removes it, or refuses it and keeps it renamed, without the
/// identity.
/// </summary>
public sealed record RestoreMarker(string File, Guid RequestedById, DateTimeOffset RequestedAt, string? Identity)
{
    public const string FileName = "restore.pending";

    public static string PathIn(string backupsRoot) => System.IO.Path.Combine(backupsRoot, FileName);

    /// <summary>Writes the marker, readable by the app's user only; a marker already there is replaced.</summary>
    public void Write(string backupsRoot)
    {
        Directory.CreateDirectory(backupsRoot);
        WriteTo(PathIn(backupsRoot));
    }

    /// <summary>Writes the marker at a path, readable by the app's user only before anything is in it; a file already there is replaced.</summary>
    public void WriteTo(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        // One left by an older run keeps its mode otherwise, and the identity would be written into a file others may read.
        if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        JsonSerializer.Serialize(stream, this, BackupManifest.Json);
    }

    /// <summary>The marker at a path; a <see cref="JsonException"/> when the file is not one.</summary>
    public static RestoreMarker Read(string path) =>
        JsonSerializer.Deserialize<RestoreMarker>(System.IO.File.ReadAllText(path), BackupManifest.Json) ?? throw new JsonException("The marker is empty.");
}
