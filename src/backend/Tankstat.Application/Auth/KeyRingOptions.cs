namespace Tankstat.Application.Auth;

/// <summary>
/// Bound from the "DataProtection" section: where the keys that protect the cookies (and the OIDC sign-in state, and the stored refresh
/// secrets) are kept. With them in a folder that outlives the process (the Docker image puts it next to the database, <c>/data/keys</c>),
/// a restart or a new container does not sign everybody out. An encrypted backup takes the folder along for the same reason.
/// </summary>
public sealed class KeyRingOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Relative to the working directory, like <c>Storage:Path</c>.</summary>
    public string KeysPath { get; set; } = "keys";

    /// <summary><see cref="KeysPath"/> in full, without a trailing separator.</summary>
    public string Root => Folders.Full(KeysPath);
}
