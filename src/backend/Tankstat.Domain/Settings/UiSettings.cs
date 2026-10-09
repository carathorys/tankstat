using System.Text.RegularExpressions;

namespace Tankstat.Domain.Settings;

/// <summary>
/// What the UI remembers for one user on every device: one row per user (the anonymous user's id when authentication is off). Null means
/// the user has not chosen, and the browser's own default applies. Shape and limits only: which languages exist is the UI's business.
/// </summary>
public sealed partial class UiSettings
{
    /// <summary>Width of the column; the pattern itself allows at most five characters ("en-GB").</summary>
    public const int MaxLanguageLength = 8;

    private UiSettings() { } // EF Core

    public Guid UserId { get; private set; }

    /// <summary>Whether the docked navigation (desktop) is open.</summary>
    public bool? NavOpen { get; private set; }

    /// <summary>The UI language, e.g. "en" or "en-GB".</summary>
    public string? Language { get; private set; }

    /// <summary>Light, dark or the device's own setting (null: never chosen, the UI's default applies).</summary>
    public ColorMode? ColorMode { get; private set; }

    /// <summary>Glossy, transparent or opaque surfaces (null: never chosen, glossy).</summary>
    public SurfaceStyle? Surface { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UiSettings Create(Guid userId, DateTimeOffset now) => new() { UserId = userId, UpdatedAt = now };

    public void SetNavOpen(bool open, DateTimeOffset now)
    {
        NavOpen = open;
        UpdatedAt = now;
    }

    /// <summary>Null forgets the choice; otherwise the (trimmed) code must look like "en" or "en-GB".</summary>
    public void SetLanguage(string? language, DateTimeOffset now)
    {
        var code = language?.Trim();
        if (code is not null && !LanguagePattern().IsMatch(code))
            throw new DomainException("settings.languageInvalid", "The language code must look like en or en-GB.");
        Language = code;
        UpdatedAt = now;
    }

    /// <summary>Only the defined modes: a number cast to the enum would otherwise get through.</summary>
    public void SetColorMode(ColorMode mode, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode))
            throw new DomainException("settings.colorModeInvalid", "The colour mode must be light, dark or system.");
        ColorMode = mode;
        UpdatedAt = now;
    }

    /// <summary>Only the defined styles, like the colour mode.</summary>
    public void SetSurface(SurfaceStyle surface, DateTimeOffset now)
    {
        if (!Enum.IsDefined(surface))
            throw new DomainException("settings.surfaceInvalid", "The surface style must be glossy, transparent or opaque.");
        Surface = surface;
        UpdatedAt = now;
    }

    [GeneratedRegex("^[a-z]{2}(-[A-Z]{2})?$")]
    private static partial Regex LanguagePattern();
}
