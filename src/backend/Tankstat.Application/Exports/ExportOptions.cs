using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;

namespace Tankstat.Application.Exports;

/// <summary>
/// Bound from the "Exports" section (<c>Exports__Enabled=false</c>, <c>Exports__KeepHours=48</c>, <c>Exports__Folder=/data/exports</c>):
/// Export my data (#341), a person's own cars as a file.
/// </summary>
public sealed class ExportOptions
{
    public const string SectionName = "Exports";
    public const int MinKeepHours = 1;
    public const int MaxKeepHours = 720;

    /// <summary>Whether users may export their own cars; off, only administrators can (the owner's decision on #334).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many hours a finished export is kept for its download before it is deleted.</summary>
    public int KeepHours { get; set; } = 24;

    /// <summary>
    /// Where finished exports wait for their download, and the record of each (the owner's decision 10: kept across restarts). An
    /// environment-level setting, like every folder; never inside the backups folder or a storage root, which a backup copies.
    /// </summary>
    public string Folder { get; set; } = "exports";

    public string Root => Path.GetFullPath(Folder);

    public TimeSpan Keep => TimeSpan.FromHours(KeepHours);
}

internal sealed class ExportOptionsValidator(IOptions<StorageOptions> storage, IOptions<BackupOptions> backups) : IValidateOptions<ExportOptions>
{
    public ValidateOptionsResult Validate(string? name, ExportOptions o)
    {
        var failures = new List<string>();
        if (o.KeepHours is < ExportOptions.MinKeepHours or > ExportOptions.MaxKeepHours)
            failures.Add($"Exports:KeepHours must be between {ExportOptions.MinKeepHours} and {ExportOptions.MaxKeepHours}.");
        if (string.IsNullOrWhiteSpace(o.Folder)) failures.Add("Exports:Folder must not be empty.");
        else
            foreach (var (setting, root) in Others())
                if (Folders.Overlap(o.Root, root)) failures.Add($"Exports:Folder and {setting} must be two separate folders, not the same one or one inside the other.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>The folders a backup copies or writes, read as their own validators allow (a failure there is reported by them).</summary>
    private IEnumerable<(string Setting, string Root)> Others()
    {
        var list = new List<(string, string)>();
        try
        {
            var s = storage.Value;
            list.Add(("Storage:Path", s.PicturesRoot));
            if (!s.OneRoot) list.Add(("Storage:PhotosPath", s.PhotosRoot));
        }
        catch (OptionsValidationException) { }
        try
        {
            list.Add(("Backups:Path", backups.Value.Root));
        }
        catch (OptionsValidationException) { }
        return list;
    }
}
