namespace Tankstat.Application.Setup;

/// <summary>
/// Bound from the "Setup" section (<c>Setup__Enabled=false</c>). An environment setting: not on the Server settings page, which needs the
/// administrator the setup makes.
/// </summary>
public sealed class SetupOptions
{
    public const string SectionName = "Setup";

    /// <summary>
    /// False: a new instance counts as set up at once (completed by the environment) and never shows the setup page, for hosts that have
    /// nothing to set up in a browser. <c>Auth:AdminEmails</c> in OIDC and proxy mode does the same without this. Standalone without an
    /// administrator does not start with it: only the setup page makes the first one.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
