namespace Tankstat.Application.Images;

/// <summary>Bound from the "Storage" section (e.g. <c>Storage__Path=/data/uploads</c>).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded pictures; created when needed. Relative paths start at the working directory.</summary>
    public string Path { get; set; } = "uploads";
}
