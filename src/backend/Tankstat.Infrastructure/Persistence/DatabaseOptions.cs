using System.ComponentModel.DataAnnotations;

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
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public DatabaseProvider Provider { get; init; }

    [Required]
    public string ConnectionString { get; init; } = "";
}
