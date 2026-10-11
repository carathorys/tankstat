namespace Tankstat.Domain.Instance;

/// <summary>
/// One row about this installation, written at every start: the authentication mode the app last started in, so a start in another mode
/// can sign every device out (a session issued in one mode must not keep working in the next). Other facts about the instance can join it.
/// </summary>
public sealed class InstanceState
{
    public const int SingletonId = 1;
    public const int MaxModeLength = 20;

    private InstanceState() { }

    public int Id { get; private set; } = SingletonId;

    /// <summary>The authentication mode of the last start, by name; null before the first start that recorded one.</summary>
    public string? LastStartMode { get; private set; }

    public DateTimeOffset? LastStartedAt { get; private set; }

    public static InstanceState New() => new();

    public void RecordStart(string mode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Length > MaxModeLength) throw new DomainException("instance.modeInvalid", "The authentication mode must be named.");
        LastStartMode = mode;
        LastStartedAt = now;
    }
}
