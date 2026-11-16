namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// Which of a backup's tables the reader (#336) loads into its scratch database, by name, as an allowlist: the cars and what belongs to
/// them. Everything else stays an empty table there, the secrets above all (sessions, reset tokens) and what is a person's or the
/// instance's own (settings, notifications, the sync ledger). The names are those of every migration this release knows, the ones a
/// table had before a rename too, so an older backup's rows are neither dropped nor loaded by mistake; a table in neither list fails
/// <c>ScratchTablesTests</c>, so a new one is sorted when it is added.
/// </summary>
internal static class ScratchTables
{
    public const string Users = "Users";

    /// <summary>Loaded as they are.</summary>
    public static readonly IReadOnlySet<string> Loaded = new HashSet<string>(StringComparer.Ordinal)
    {
        "Vehicles", "Refuelings", "Expenses", "Costs", "OdometerReadings", "RecurringExpenses", "RecurringCompletions",
        "TyreSets", "TyreInstallations", "TyreEntries", "Tyres", "TyrePlaces",
        "TyreStretches", "TyreLogDays", // what the installations and the tyre log's lines were before v0.21 renamed and rewrote them
        "LogPhotos", "Images", "VehicleCharts", "VehicleDashboardLayouts", "UserDashboardLayouts", "ResourceGrants",
    };

    /// <summary>Never loaded: left empty in the scratch database.</summary>
    public static readonly IReadOnlySet<string> Never = new HashSet<string>(StringComparer.Ordinal)
    {
        "AccessGrants", "AccessSettings", "GridSettings", "InstanceState", "Notifications", "OfflineSettings", "OfflineVehicleSettings",
        "PasswordResetTokens", "PhotoDrafts", "PhotoReadings", "SyncChanges", "Tombstones", "UiSettings", "UserSessions", "VehicleOrders",
    };

    /// <summary>
    /// The columns of <see cref="Users"/> that are kept: who a car was whose, by name (and how the account signs in, local or through a
    /// provider: no secret, and a row without it cannot be read). Every other column is blanked before the load (a
    /// password hash, an e-mail address, a provider's subject never reach the scratch database), the e-mail kept only where the caller
    /// may see it (an administrator's partial restore).
    /// </summary>
    public static readonly IReadOnlySet<string> UserColumns = new HashSet<string>(StringComparer.Ordinal) { "Id", "DisplayName", "Provider" };

    public const string UserEmail = "Email";

    /// <summary>Whether the table's rows are loaded (<see cref="Users"/> reduced); false for one that stays empty.</summary>
    public static bool IsLoaded(string table) => table == Users || Loaded.Contains(table);

    public static bool IsKnown(string table) => IsLoaded(table) || Never.Contains(table) || table == TableDump.MigrationsHistory;
}
