namespace Tankstat.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    PostgreSql,
    SqlServer,
    MySql,
}

/// <summary>
/// Bound from the "Database" configuration section. Standard ASP.NET Core precedence applies
/// (lowest to highest): appsettings.json, appsettings.{Environment}.json, environment variables
/// (<c>Database__Provider</c>, <c>Database__ConnectionString</c>), command line.
/// With nothing configured the app falls back to a SQLite file in the working directory.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string DefaultSqliteConnectionString = "Data Source=tankstat.db";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Required for every provider except SQLite, which defaults to <see cref="DefaultSqliteConnectionString"/>.</summary>
    public string? ConnectionString { get; set; }
}
