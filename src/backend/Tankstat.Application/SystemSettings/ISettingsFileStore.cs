namespace Tankstat.Application.SystemSettings;

/// <summary>
/// The app's own settings file on disk, as flat configuration keys (<c>Auth:Mode</c>, <c>Auth:AdminEmails:0</c>): read as the configuration
/// reads it, written as a whole. Writing keeps the previous file beside it and never leaves a half-written one.
/// </summary>
public interface ISettingsFileStore
{
    /// <summary>The file, in full.</summary>
    string Path { get; }

    /// <summary>The file's values by configuration key (sections left out); empty when there is no file.</summary>
    Task<IReadOnlyDictionary<string, string?>> ReadAsync(CancellationToken ct);

    /// <summary>Replaces the file with <paramref name="values"/> (the previous one is kept as <c>.bak</c>), readable by the owner only.</summary>
    Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct);
}
