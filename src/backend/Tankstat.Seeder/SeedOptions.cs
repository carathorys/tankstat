namespace Tankstat.Seeder;

public readonly record struct IntRange(int Min, int Max)
{
    public override string ToString() => Min == Max ? Min.ToString() : $"{Min}-{Max}";
}

public sealed record SeedOptions(
    int Vehicles,
    int Trashed,
    IntRange RefuelingsPerVehicle,
    int RandomSeed,
    bool AssumeYes,
    string? Provider,
    string? ConnectionString);

public sealed class SeedOptionsException(string message) : Exception(message);

public static class SeedOptionsParser
{
    public const int MaxVehicles = 1_000_000;
    public const int MaxRefuelingsPerVehicle = 10_000;

    public const string Usage = """
        Tankstat database seeder (for testing only; authentication mode None only).

        Deletes the database, creates it, applies the migrations and fills it with random but consistent data:
        vehicles, optionally some in the trash, and a fuel log for each (dates and odometer only ever increase, litres
        follow a per-vehicle consumption). It creates no users; everything belongs to the anonymous (no-auth) owner.

        Usage: Tankstat.Seeder [options]

          --vehicles <n>         vehicles to create                                  (default 25)
          --trashed <n>          additional vehicles that are in the trash           (default 0)
          --refuelings <n|a-b>   refuelings per vehicle, e.g. 20 or a range 5-40     (default 20)
          --seed <n>             random seed; the same seed gives the same data      (default 1234)
          --provider <name>      Sqlite, PostgreSql, SqlServer or MySql              (default: Database:Provider, else Sqlite)
          --connection <string>  database connection string                          (default: Database:ConnectionString,
                                                                                      else Data Source=tankstat.db)
          --yes                  do not ask for confirmation before deleting the database
          --help                 show this text

        The database settings can also come from the usual environment variables (Database__Provider,
        Database__ConnectionString). The seeder refuses to run when Auth__Mode is anything but None.
        """;

    /// <summary>Returns null when help was requested.</summary>
    public static SeedOptions? Parse(IReadOnlyList<string> args)
    {
        int vehicles = 25, trashed = 0, seed = 1234;
        var refuelings = new IntRange(20, 20);
        var yes = false;
        string? provider = null, connection = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? inline = null;
            var eq = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 0) (arg, inline) = (arg[..eq], arg[(eq + 1)..]);

            string Value()
            {
                if (inline is not null) return inline;
                if (i + 1 >= args.Count) throw new SeedOptionsException($"{arg} needs a value.");
                return args[++i];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h" or "-?": return null;
                case "--yes" or "-y": yes = true; break;
                case "--vehicles": vehicles = Count(arg, Value(), MaxVehicles); break;
                case "--trashed": trashed = Count(arg, Value(), MaxVehicles); break;
                case "--refuelings": refuelings = Range(arg, Value()); break;
                case "--seed": seed = int.TryParse(Value(), out var s) ? s : throw new SeedOptionsException("--seed must be a whole number."); break;
                case "--provider": provider = Value(); break;
                case "--connection": connection = Value(); break;
                default: throw new SeedOptionsException($"Unknown option '{arg}'. Use --help.");
            }
        }

        if ((long)vehicles + trashed > MaxVehicles) throw new SeedOptionsException($"At most {MaxVehicles:N0} vehicles in total.");
        return new SeedOptions(vehicles, trashed, refuelings, seed, yes, provider, connection);
    }

    private static int Count(string name, string text, int max)
    {
        if (!int.TryParse(text, out var n) || n < 0 || n > max) throw new SeedOptionsException($"{name} must be a whole number from 0 to {max:N0}.");
        return n;
    }

    private static IntRange Range(string name, string text)
    {
        var parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], out var min) || (parts.Length == 2 && !int.TryParse(parts[1], out _)))
            throw new SeedOptionsException($"{name} must be a number (20) or a range (5-40).");
        var max = parts.Length == 2 ? int.Parse(parts[1]) : min;
        if (min < 0 || max < min || max > SeedOptionsParser.MaxRefuelingsPerVehicle)
            throw new SeedOptionsException($"{name} must be between 0 and {SeedOptionsParser.MaxRefuelingsPerVehicle:N0}, and the range must not be reversed.");
        return new IntRange(min, max);
    }
}
