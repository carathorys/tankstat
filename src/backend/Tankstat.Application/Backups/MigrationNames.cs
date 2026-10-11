namespace Tankstat.Application.Backups;

/// <summary>
/// A migration is told apart by its name, not by its id. EF stamps each provider's copy of a migration with its own timestamp
/// (<c>20261001054544_InitialCreate</c> on SQLite, <c>20261001054547_InitialCreate</c> on PostgreSQL, ...), because <c>mise run db:migration</c>
/// generates the four one after the other; the names and their order are the same on all of them (<c>MigrationsTests</c> pins that). A backup
/// is compared with the database by the name of its last migration, which is what lets a backup move from one provider to another.
/// </summary>
public static class MigrationNames
{
    /// <summary>The name of a migration id: what follows the timestamp (<c>InitialCreate</c>); the id itself when it has no timestamp.</summary>
    public static string? Of(string? id)
    {
        if (id is null) return null;
        var underscore = id.IndexOf('_');
        return underscore < 0 ? id : id[(underscore + 1)..];
    }

    public static bool Same(string? a, string? b) => string.Equals(Of(a), Of(b), StringComparison.Ordinal);
}
