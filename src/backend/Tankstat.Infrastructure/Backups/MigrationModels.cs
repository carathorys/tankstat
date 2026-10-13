using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The migrations this release knows on the database's provider, found by name (<see cref="MigrationNames"/>: a backup may come from
/// another provider, whose copy of a migration has another timestamp), and the model each one leaves the database in: the snapshot EF keeps
/// with every migration, made usable the way EF's own migrator makes it (finalized, then initialized at design time, so its columns have
/// the provider's type mappings). A backup made at an older migration is dumped and loaded with that model.
/// </summary>
internal static class MigrationModels
{
    /// <summary>This release's migrations on the provider, oldest first, by their ids.</summary>
    public static IReadOnlyList<string> Ids(AppDbContext db) => [.. db.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The provider's id of the migration named as <paramref name="migration"/> is (its id on any provider); null when this release has none so named.</summary>
    public static string? IdOf(AppDbContext db, string? migration) =>
        migration is null ? null : Ids(db).FirstOrDefault(id => MigrationNames.Same(id, migration));

    /// <summary>The model the database is in once the migration <paramref name="id"/> (this provider's) and every one before it ran.</summary>
    public static IModel ModelOf(AppDbContext db, string id)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
        var model = migration.TargetModel ?? throw new InvalidOperationException($"The migration {id} has no model.");
        if (model is IMutableModel mutable) model = mutable.FinalizeModel();
        return db.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true, validationLogger: null);
    }
}
