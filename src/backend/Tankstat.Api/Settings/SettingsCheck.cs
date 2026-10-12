using Microsoft.Extensions.Options;
using Tankstat.Api.Auth;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.SystemSettings;
using Tankstat.Infrastructure;

namespace Tankstat.Api.Settings;

/// <summary>
/// Whether the app could start with a configuration: the same checks the host runs, without a host. The managed settings' shapes are checked
/// first (so a wrong number is named by its setting; photo reading, whose settings never stop the app, and the log levels' floor, a rule for
/// what the page writes, are not among them), and every <c>Logging</c> level, nested ones too (a wrong one stops <c>Build()</c>); then what the
/// host itself refuses, through a throwaway container with the app's own registrations: the logging set-up <c>Build()</c> makes (the filter,
/// the console's options and its formatters'), every option class the app validates at start (<see cref="IStartupValidator"/>), plus
/// <c>Smtp</c>, which the app binds without validating. <see cref="Check"/> answers for one configuration, <see cref="Added"/> for what a
/// source adds to the others, <see cref="Starts"/> whether the host alone would start, <see cref="AgainstFile"/> what the next start would
/// hold against a settings file (the system settings page's question). The problems name settings and rules, never values: the binder
/// quotes a value in its exception, so only the path is taken from it.
/// </summary>
public static class SettingsCheck
{
    public static IReadOnlyList<string> Check(IConfiguration configuration)
    {
        var shapes = Shapes(configuration);
        return shapes.Count > 0 ? shapes : Refusals(configuration); // a value of the wrong shape would be reported by the binder too, with the value in its text
    }

    /// <summary>
    /// The problems <paramref name="configuration"/> has that <paramref name="baseline"/> (the same sources without one of them) has not: what
    /// the baseline gets wrong on its own is not the other source's doing, and must neither count against it nor hide what it gets wrong (the
    /// shapes and the refusals are compared each on their own, since <see cref="Check"/> stops at the shapes).
    /// </summary>
    public static IReadOnlyList<string> Added(IConfiguration configuration, IConfiguration baseline)
    {
        var shapes = Shapes(configuration).Except(Shapes(baseline)).ToList();
        return shapes.Count > 0 ? shapes : [.. Refusals(configuration).Except(Refusals(baseline))];
    }

    /// <summary>
    /// Whether the host itself would start with <paramref name="configuration"/>: only what it refuses (the logging set-up, the options it
    /// validates at start), none of the stricter rules the check adds for the settings file (the shapes, <c>Smtp</c> bound in every mode).
    /// </summary>
    public static bool Starts(IConfiguration configuration) => Refusals(configuration, smtp: false).Count == 0;

    /// <summary>
    /// What the next start would hold against a settings file, <paramref name="with"/> being the whole configuration with its values in the
    /// file's place and <paramref name="without"/> the same sources with no file: the problems the file adds (the start would leave it out,
    /// or fail with it), else what the host itself would refuse with it (the environment may lean on the file: an OIDC mode whose authority
    /// only the file holds). A mistake of the environment's own that the host starts with anyway is not the file's, as at start
    /// (<see cref="SettingsFile.Prepare(ConfigurationManager)"/>): it leaves no file out, so it holds no save back either.
    /// </summary>
    public static IReadOnlyList<string> AgainstFile(IConfiguration with, IConfiguration without)
    {
        var added = Added(with, without);
        return added.Count > 0 ? added : Refusals(with, smtp: false);
    }

    private static List<string> Shapes(IConfiguration configuration) =>
        [.. SettingsCatalog.Problems(key => configuration[key]), .. LoggingProblems(configuration)];

    private static List<string> Refusals(IConfiguration configuration, bool smtp = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")).AddConsole()); // what Build() reads, see below
        services.AddSingleton(configuration);
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);
        services.AddAuthModes();
        using var provider = services.BuildServiceProvider();
        var problems = new List<string>();
        // The logger factory reads Logging:CaptureScopes, the levels and the console's options and its formatters' as the host is built: a
        // value it cannot bind stops the start before anything runs. The binder names those of the console relative to their section.
        Try(() => provider.GetRequiredService<ILoggerFactory>(), "Logging:Console");
        Try(() => provider.GetService<IStartupValidator>()?.Validate());
        if (smtp) Try(() => _ = provider.GetRequiredService<IOptions<SmtpOptions>>().Value);
        return [.. problems.Distinct()]; // a Smtp value the binder refuses fails the Auth validator (which reads Smtp) and the binding of Smtp alike

        void Try(Action check, string? section = null)
        {
            try
            {
                check();
            }
            catch (Exception e)
            {
                problems.AddRange(ProblemsOf(e, section));
            }
        }
    }

    /// <summary>
    /// Every value under <c>Logging:LogLevel</c> and <c>Logging:{Provider}:LogLevel</c>, nested ones too, must be a log level, as the logging
    /// filter insists when the host is built (it skips an empty one). The catalog's own levels are checked with the rest of the catalog.
    /// </summary>
    private static IEnumerable<string> LoggingProblems(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("Logging").GetChildren())
        {
            var levels = section.Key.Equals("LogLevel", StringComparison.OrdinalIgnoreCase) ? section : section.GetSection("LogLevel");
            foreach (var (relative, value) in levels.AsEnumerable(makePathsRelative: true))
            {
                var key = $"{levels.Path}:{relative}";
                if (string.IsNullOrEmpty(value) || SettingsCatalog.Find(key) is not null) continue;
                if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(value, ignoreCase: true, out _))
                    yield return $"{key} must be one of {string.Join(", ", SettingsCatalog.LogLevels)}.";
            }
        }
    }

    private static IEnumerable<string> ProblemsOf(Exception e, string? section) => e switch
    {
        OptionsValidationException validation => validation.Failures,
        AggregateException aggregate => aggregate.InnerExceptions.SelectMany(inner => ProblemsOf(inner, section)),
        _ => [Unreadable(e, section)],
    };

    /// <summary>
    /// The binder says "Failed to convert configuration value 'x' at 'Section:Key' to type 'T'": the path is kept, the value is not. The value
    /// comes first and may hold anything, the words around the path included, so the path is the last one before the type.
    /// </summary>
    private static string Unreadable(Exception e, string? section)
    {
        var text = e.Message;
        var end = text.LastIndexOf("' to type '", StringComparison.Ordinal);
        var at = end > 0 ? text.LastIndexOf(" at '", end, StringComparison.Ordinal) : -1;
        if (at < 0 || at + " at '".Length > end) return "A setting cannot be read as a value of its type.";
        var path = text[(at + " at '".Length)..end];
        if (section is not null && !path.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase)) path = $"{section}:{path}";
        return $"{path} cannot be read as a value of its type.";
    }
}
