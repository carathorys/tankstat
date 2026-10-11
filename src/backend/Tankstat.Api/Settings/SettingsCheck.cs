using Microsoft.Extensions.Options;
using Tankstat.Api.Auth;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.SystemSettings;
using Tankstat.Infrastructure;

namespace Tankstat.Api.Settings;

/// <summary>
/// Whether the app could start with a configuration: the same checks the host runs, without a host. The managed settings' kinds are checked
/// first (so a wrong number is named by its setting), then every <c>Logging</c> level (a wrong one stops <c>Build()</c>), then every option
/// class the app validates at start, through a throwaway container with the app's own registrations (<see cref="IStartupValidator"/>), plus
/// <c>Smtp</c>, which the app binds without validating. Photo reading is left out on purpose: its settings never stop the app. The problems
/// name settings and rules, never values: the binder quotes a value in its exception, so only the path is taken from it.
/// </summary>
public static class SettingsCheck
{
    public static IReadOnlyList<string> Check(IConfiguration configuration)
    {
        var problems = new List<string>(SettingsCatalog.Problems(key => configuration[key]));
        problems.AddRange(LoggingProblems(configuration));
        if (problems.Count > 0) return problems; // a value of the wrong shape would be reported by the binder too, with the value in its text

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);
        services.AddAuthModes();
        using var provider = services.BuildServiceProvider();
        try
        {
            provider.GetService<IStartupValidator>()?.Validate();
            _ = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;
        }
        catch (Exception e)
        {
            problems.AddRange(ProblemsOf(e));
        }
        return problems;
    }

    /// <summary>Every <c>Logging:LogLevel:*</c> and <c>Logging:{Provider}:LogLevel:*</c> value must be a log level, as the logging filters insist when the host is built.</summary>
    private static IEnumerable<string> LoggingProblems(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("Logging").GetChildren())
        {
            var levels = section.Key.Equals("LogLevel", StringComparison.OrdinalIgnoreCase) ? section : section.GetSection("LogLevel");
            foreach (var level in levels.GetChildren())
                if (level.Value is { } value && !Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(value, ignoreCase: true, out _))
                    yield return $"{level.Path} must be one of {string.Join(", ", SettingsCatalog.LogLevels)}.";
        }
    }

    private static IEnumerable<string> ProblemsOf(Exception e) => e switch
    {
        OptionsValidationException validation => validation.Failures,
        AggregateException aggregate => aggregate.InnerExceptions.SelectMany(ProblemsOf),
        _ => [Unreadable(e)],
    };

    /// <summary>The binder says "Failed to convert configuration value 'x' at 'Section:Key' to type 'T'": the path is kept, the value is not.</summary>
    private static string Unreadable(Exception e)
    {
        var text = e.Message;
        var at = text.IndexOf(" at '", StringComparison.Ordinal);
        if (at >= 0)
        {
            var start = at + " at '".Length;
            var end = text.IndexOf('\'', start);
            if (end > start) return $"{text[start..end]} cannot be read as a value of its type.";
        }
        return "A setting cannot be read as a value of its type.";
    }
}
