namespace Tankstat.Domain.Instance;

/// <summary>Where the instance's setup stands (see <see cref="InstanceState.Setup"/>).</summary>
public enum SetupPhase
{
    /// <summary>Not decided yet: a row from before setup existed. The start decides it: data means the instance was set up long ago.</summary>
    Undecided,

    /// <summary>It waits for its first administrator, or with sign-in off for the acknowledgement; the setup page and its code are open.</summary>
    Pending,

    Completed,
}

/// <summary>How the setup was completed.</summary>
public enum SetupCompletion
{
    /// <summary>The database held data when setup first came: an instance set up before there was a setup.</summary>
    Upgrade,

    /// <summary>The environment did it: the bootstrap administrator, <c>Auth:AdminEmails</c>, or <c>Setup:Enabled=false</c>.</summary>
    Environment,

    /// <summary>The first administrator was made, or claimed, on the setup page.</summary>
    Administrator,

    /// <summary>With sign-in off, the open access was acknowledged on the setup page.</summary>
    Acknowledged,
}
