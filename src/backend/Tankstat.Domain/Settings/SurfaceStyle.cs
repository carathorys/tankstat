namespace Tankstat.Domain.Settings;

/// <summary>
/// How the UI's floating surfaces (top bar, navigation, dialogs, menus, cards, messages) are drawn: glossy (see-through and blurred),
/// transparent (see-through, not blurred) or opaque.
/// </summary>
public enum SurfaceStyle
{
    Glossy,
    Transparent,
    Opaque,
}
