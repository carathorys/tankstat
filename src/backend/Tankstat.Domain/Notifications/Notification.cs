namespace Tankstat.Domain.Notifications;

/// <summary>
/// Something a user is told about, kept until they delete it. It is attached to a <see cref="Subject"/> (what it is about; together with
/// the topic, the context and the occurrence also its identity) and optionally a <see cref="Context"/> (where it leads, e.g. the vehicle
/// of a schedule). The arguments are display-ready values taken when it was created or last updated, so it still reads right after a
/// rename or a deletion. Several events about the same thing may be folded into one (<see cref="Count"/>); see <see cref="NotificationPolicy"/>.
/// </summary>
public sealed class Notification
{
    public const int MaxOccurrenceLength = 64;
    public const int MaxArgs = 10;
    public const int MaxArgNameLength = 32;
    public const int MaxArgValueLength = 200;
    public const int MaxValueLength = 32;

    private Notification() { } // EF Core

    public Guid Id { get; private set; }
    public Guid RecipientId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public NotificationTopic Topic { get; private set; }
    public NotificationEntityType SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }

    /// <summary>Null when the notification has no context.</summary>
    public NotificationEntityType? ContextType { get; private set; }

    /// <summary><see cref="Guid.Empty"/> when there is no context (never null, so the unique identity behaves alike on every database).</summary>
    public Guid ContextId { get; private set; }

    /// <summary>
    /// Tells occurrences of the same subject apart. Derived notifications (worked out on read, e.g. a schedule's cycle) use a value that
    /// is the same every time, so they can exist only once; events use a fresh value.
    /// </summary>
    public string Occurrence { get; private set; } = "";

    public IReadOnlyDictionary<string, string> Args { get; private set; } = new Dictionary<string, string>();

    /// <summary>The value before the first folded event (e.g. the old access level), so a change that is undone again can be dropped.</summary>
    public string? Before { get; private set; }

    /// <summary>How many events this notification stands for.</summary>
    public int Count { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it last changed (created, folded or raised); the inbox is ordered by it.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public NotificationRef Subject => new(SubjectType, SubjectId);
    public NotificationRef? Context => ContextType is { } type ? new NotificationRef(type, ContextId) : null;
    public bool IsRead => ReadAt is not null;

    public static Notification Create(
        Guid recipientId, NotificationKind kind, NotificationRef subject, NotificationRef? context, string occurrence,
        IReadOnlyDictionary<string, string> args, string? before, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(occurrence) || occurrence.Length > MaxOccurrenceLength)
            throw new ArgumentException($"The occurrence must be 1-{MaxOccurrenceLength} characters.", nameof(occurrence));
        if (before is { Length: > MaxValueLength }) throw new ArgumentException($"The value before can be at most {MaxValueLength} characters.", nameof(before));
        return new Notification
        {
            Id = Guid.NewGuid(),
            RecipientId = recipientId,
            Kind = kind,
            Topic = NotificationKinds.TopicOf(kind),
            SubjectType = subject.Type,
            SubjectId = subject.Id,
            ContextType = context?.Type,
            ContextId = context?.Id ?? Guid.Empty,
            Occurrence = occurrence,
            Args = CheckedArgs(args),
            Before = before,
            Count = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Takes in one more event of the same topic: the latest kind and arguments win, and it counts one more.</summary>
    public void Fold(NotificationKind kind, IReadOnlyDictionary<string, string> args, DateTimeOffset now)
    {
        CheckSameTopic(kind);
        Kind = kind;
        Args = CheckedArgs(args);
        Count++;
        UpdatedAt = now;
    }

    /// <summary>Becomes the more urgent <paramref name="kind"/> of the same topic and is unread again, so the recipient hears about it.</summary>
    public void Raise(NotificationKind kind, IReadOnlyDictionary<string, string> args, DateTimeOffset now)
    {
        CheckSameTopic(kind);
        Kind = kind;
        Args = CheckedArgs(args);
        UpdatedAt = now;
        ReadAt = null;
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private void CheckSameTopic(NotificationKind kind)
    {
        if (NotificationKinds.TopicOf(kind) != Topic) throw new InvalidOperationException($"{kind} does not belong to the topic {Topic}.");
    }

    private static Dictionary<string, string> CheckedArgs(IReadOnlyDictionary<string, string> args)
    {
        if (args.Count > MaxArgs) throw new ArgumentException($"At most {MaxArgs} arguments.", nameof(args));
        foreach (var (name, value) in args)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxArgNameLength) throw new ArgumentException($"Bad argument name '{name}'.", nameof(args));
            if (value.Length > MaxArgValueLength) throw new ArgumentException($"The argument '{name}' is too long.", nameof(args));
        }
        return new Dictionary<string, string>(args);
    }
}
