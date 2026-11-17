using Microsoft.Extensions.Options;

namespace Tankstat.Application.Imports;

/// <summary>
/// Bound from the "Imports" section (<c>Imports__BackupEnabled=false</c>, <c>Imports__MaxUploadMb=200</c>,
/// <c>Imports__ScratchFolder=/data/import-scratch</c>): importing cars out of a backup-shaped file (#334).
/// </summary>
public sealed class BackupImportOptions
{
    public const string SectionName = "Imports";
    public const int MinUploadMb = 1;
    public const int MaxUploadMbLimit = 102_400;

    /// <summary>Whether users may import from a backup or an export; off, only administrators can.</summary>
    public bool BackupEnabled { get; set; } = true;

    /// <summary>The largest backup or export a person may upload to import from, in megabytes.</summary>
    public int MaxUploadMb { get; set; } = 200;

    /// <summary>
    /// Where an uploaded file and what is unpacked from it wait until the import is done or given up: a folder of the app's own is made
    /// in it. Unset: the system's temporary folder, which on some machines is in memory. An environment-level setting, like every folder.
    /// </summary>
    public string? ScratchFolder { get; set; }

    public long MaxUploadBytes => MaxUploadMb * 1024L * 1024L;
}

internal sealed class BackupImportOptionsValidator : IValidateOptions<BackupImportOptions>
{
    public ValidateOptionsResult Validate(string? name, BackupImportOptions o) =>
        o.MaxUploadMb is < BackupImportOptions.MinUploadMb or > BackupImportOptions.MaxUploadMbLimit
            ? ValidateOptionsResult.Fail($"Imports:MaxUploadMb must be between {BackupImportOptions.MinUploadMb} and {BackupImportOptions.MaxUploadMbLimit}.")
            : ValidateOptionsResult.Success;
}
