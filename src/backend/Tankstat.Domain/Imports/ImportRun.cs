namespace Tankstat.Domain.Imports;

/// <summary>What a run brings in: a person's import from a backup, or an administrator's partial restore (#340).</summary>
public enum ImportRunKind { Import, Restore }

/// <summary>Where a run stands. One runs at a time on the instance; the others wait in line and start by themselves.</summary>
public enum ImportRunState { Waiting, Running, Done, NotFinished }

/// <summary>Why a run brought nothing in.</summary>
public enum ImportRunFailure
{
    /// <summary>Something unexpected: rolled back.</summary>
    Failed,
    /// <summary>Cancelled by its starter or an administrator.</summary>
    Cancelled,
    /// <summary>It held the transaction longer than the server allows.</summary>
    TimedOut,
    /// <summary>The server stopped while it ran or waited.</summary>
    Interrupted,
    /// <summary>It waited in line longer than a run may.</summary>
    DidNotStart,
}

/// <summary>
/// A run of an import from a backup (#337): from the moment the person presses Import until its report is no longer kept. It is the
/// run's record for people (the queue, the progress, the report), where the run scope's mark (<c>RunMarker</c>, the same id) is the
/// record for the database's other saves. It names its starter by id without a foreign key and is deleted with the account. Its plan
/// and its report are JSON texts the application writes and reads: car names and, in the report, each entry that could not be saved
/// with its reason. They are the starter's own data, read by them alone.
/// </summary>
public sealed class ImportRun
{
    public const int MaxJsonLength = 1_000_000;
    public const int MaxServerLength = 64;

    private ImportRun() { } // EF Core

    public Guid Id { get; private set; }
    public ImportRunKind Kind { get; private set; }
    public Guid StartedById { get; private set; }
    public ImportRunState State { get; private set; }
    public ImportRunFailure? Failure { get; private set; }

    /// <summary>Who cancelled it, when someone did.</summary>
    public Guid? CancelledById { get; private set; }

    /// <summary>When the person pressed Import: its place in the line.</summary>
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>The server process that holds the run's file, and when it last said it was there (a waiting run of a server that is gone never starts).</summary>
    public string Server { get; private set; } = "";
    public DateTimeOffset SeenAt { get; private set; }

    /// <summary>The cars chosen, their targets and policies.</summary>
    public string Plan { get; private set; } = "";

    public int Cars { get; private set; }
    public int Entries { get; private set; }

    /// <summary>How far it is, on a database that can be written beside the run (never on SQLite, where the server keeps it in memory).</summary>
    public int DoneCars { get; private set; }
    public int DoneEntries { get; private set; }

    /// <summary>What came of it, once done.</summary>
    public string? Report { get; private set; }

    public bool IsActive => State is ImportRunState.Waiting or ImportRunState.Running;

    public static ImportRun Queue(Guid id, ImportRunKind kind, Guid startedById, string server, string plan, int cars, int entries, DateTimeOffset now) => new()
    {
        Id = id, Kind = kind, StartedById = startedById, State = ImportRunState.Waiting, QueuedAt = now, SeenAt = now, Server = server, Plan = plan, Cars = cars, Entries = entries,
    };

    public void Complete(string report, int doneCars, int doneEntries, DateTimeOffset now)
    {
        State = ImportRunState.Done;
        Report = report;
        DoneCars = doneCars;
        DoneEntries = doneEntries;
        EndedAt = now;
    }
}
