using System.Globalization;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;

namespace Tankstat.Application.SystemSettings;

/// <summary>The groups of the system settings, in the order they are shown.</summary>
public enum SettingsGroup
{
    Auth,
    Smtp,
    Defaults,
    Notifications,
    Sync,
    PhotoReading,
    Logging,
}

/// <summary>What a setting's value is, which decides how it is checked and edited.</summary>
public enum SettingKind
{
    Text,
    Multiline,
    Integer,
    Decimal,
    Boolean,
    /// <summary>One of <see cref="SettingDefinition.Choices"/>, matched without regard to case.</summary>
    Choice,
    /// <summary>Several strings (<c>Key:0</c>, <c>Key:1</c>, ... in the configuration).</summary>
    List,
    /// <summary>A password, secret or API key: stored like any text, never shown back.</summary>
    Secret,
}

/// <summary>One setting the app manages in its own settings file; <see cref="SettingsCatalog"/> lists them all.</summary>
public sealed record SettingDefinition(SettingsGroup Group, string Key, SettingKind Kind, IReadOnlyList<string>? Choices = null)
{
    public bool IsSecret => Kind == SettingKind.Secret;
    public bool IsList => Kind == SettingKind.List;
}

/// <summary>
/// Every setting the system settings page manages: the typed options classes' values that an administrator may change (the database, the
/// folders, the key ring and the file's own path stay with the environment), each with its kind and, for a choice, its values. The order is
/// the page's order. The catalog is pinned to the options classes by a test, so a property added to one of them is either listed here or
/// left out on purpose (<see cref="LeftOut"/>).
/// </summary>
public static class SettingsCatalog
{
    /// <summary>The log level categories the page offers; the shipped <c>appsettings.json</c> sets exactly these.</summary>
    public const string LoggingDefault = "Logging:LogLevel:Default";
    public const string LoggingTankstat = "Logging:LogLevel:Tankstat";
    public const string LoggingAspNetCore = "Logging:LogLevel:Microsoft.AspNetCore";
    public const string LoggingEfCore = "Logging:LogLevel:Microsoft.EntityFrameworkCore";
    public const string LoggingHttpClient = "Logging:LogLevel:System.Net.Http.HttpClient";

    /// <summary>The ASP.NET Core categories must stay at Warning or above: their request lines hold the token of a password reset link.</summary>
    public const string LogLevelFloorCategory = "Microsoft.AspNetCore";
    public const LogLevel LogLevelFloor = LogLevel.Warning;

    public static readonly IReadOnlyList<string> LogLevels = Enum.GetNames<LogLevel>();

    /// <summary>Settings of the managed sections that the page deliberately does not offer (the reflection test skips them).</summary>
    public static readonly IReadOnlyList<string> LeftOut =
    [
        "Auth:Standalone:AdminEmail", // the bootstrap administrator: read only while no administrator exists, and a password besides
        "Auth:Standalone:AdminPassword",
        "Recognition:OpenAiCompatible:SystemPromptFile", // a path the app reads: the environment's business, like every other path
    ];

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SettingsGroup.Auth, "Auth:Mode", SettingKind.Choice, Enum.GetNames<AuthMode>()),
        new(SettingsGroup.Auth, "Auth:PublicUrl", SettingKind.Text),
        new(SettingsGroup.Auth, "Auth:AdminEmails", SettingKind.List),
        new(SettingsGroup.Auth, "Auth:AccessTokenMinutes", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:RefreshTokenDays", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:RefreshRotationGraceSeconds", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:Standalone:MaxFailedAttempts", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:Standalone:LockoutMinutes", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:Standalone:ResetTokenMinutes", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:Standalone:ResetCooldownMinutes", SettingKind.Integer),
        new(SettingsGroup.Auth, "Auth:Standalone:AllowAdminSetPassword", SettingKind.Boolean),
        new(SettingsGroup.Auth, "Auth:Oidc:Authority", SettingKind.Text),
        new(SettingsGroup.Auth, "Auth:Oidc:ClientId", SettingKind.Text),
        new(SettingsGroup.Auth, "Auth:Oidc:ClientSecret", SettingKind.Secret),
        new(SettingsGroup.Auth, "Auth:Oidc:Scopes", SettingKind.List),
        new(SettingsGroup.Auth, "Auth:ProxyHeader:UserHeader", SettingKind.Text),
        new(SettingsGroup.Auth, "Auth:ProxyHeader:EmailHeader", SettingKind.Text),
        new(SettingsGroup.Auth, "Auth:ProxyHeader:TrustedProxies", SettingKind.List),

        new(SettingsGroup.Smtp, "Smtp:Host", SettingKind.Text),
        new(SettingsGroup.Smtp, "Smtp:Port", SettingKind.Integer),
        new(SettingsGroup.Smtp, "Smtp:Username", SettingKind.Text),
        new(SettingsGroup.Smtp, "Smtp:Password", SettingKind.Secret),
        new(SettingsGroup.Smtp, "Smtp:From", SettingKind.Text),
        new(SettingsGroup.Smtp, "Smtp:Security", SettingKind.Choice, ["Auto", "None", "StartTls", "Ssl"]),

        new(SettingsGroup.Defaults, "Defaults:DistanceUnit", SettingKind.Choice, Enum.GetNames<DistanceUnit>()),
        new(SettingsGroup.Defaults, "Defaults:VolumeUnit", SettingKind.Choice, Enum.GetNames<VolumeUnit>()),
        new(SettingsGroup.Defaults, "Defaults:Currency", SettingKind.Text),
        new(SettingsGroup.Defaults, "Defaults:RecurringWarnDays", SettingKind.Integer),
        new(SettingsGroup.Defaults, "Defaults:RecurringWarnDistance", SettingKind.Integer),

        new(SettingsGroup.Notifications, "Notifications:MaxPerHour", SettingKind.Integer),
        new(SettingsGroup.Notifications, "Notifications:ReadRetentionDays", SettingKind.Integer),

        new(SettingsGroup.Sync, "Sync:RetentionDays", SettingKind.Integer),
        new(SettingsGroup.Sync, "Sync:TombstoneRetentionDays", SettingKind.Integer),

        new(SettingsGroup.PhotoReading, "Recognition:Provider", SettingKind.Choice, Enum.GetNames<RecognitionProviderKind>()),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:BaseUrl", SettingKind.Text),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:Model", SettingKind.Text),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:ApiKey", SettingKind.Secret),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:SystemPrompt", SettingKind.Multiline),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:ResponseFormat", SettingKind.Choice, Enum.GetNames<OpenAiResponseFormat>()),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:Temperature", SettingKind.Decimal),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:TimeoutSeconds", SettingKind.Integer),
        new(SettingsGroup.PhotoReading, "Recognition:OpenAiCompatible:LogTraffic", SettingKind.Boolean),
        new(SettingsGroup.PhotoReading, "Recognition:MinConfidence", SettingKind.Decimal),
        new(SettingsGroup.PhotoReading, "Recognition:MaxConcurrent", SettingKind.Integer),

        new(SettingsGroup.Logging, LoggingDefault, SettingKind.Choice, LogLevels),
        new(SettingsGroup.Logging, LoggingTankstat, SettingKind.Choice, LogLevels),
        new(SettingsGroup.Logging, LoggingAspNetCore, SettingKind.Choice, LogLevels),
        new(SettingsGroup.Logging, LoggingEfCore, SettingKind.Choice, LogLevels),
        new(SettingsGroup.Logging, LoggingHttpClient, SettingKind.Choice, LogLevels),
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The groups in the page's order.</summary>
    public static IEnumerable<SettingsGroup> Groups => Enum.GetValues<SettingsGroup>();

    /// <summary>The definition of <paramref name="key"/> (case does not matter, as in the configuration), or null for a setting the page does not manage.</summary>
    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>The settings of one group, in order.</summary>
    public static IEnumerable<SettingDefinition> Of(SettingsGroup group) => All.Where(d => d.Group == group);

    /// <summary>
    /// Why <paramref name="value"/> is not a value for <paramref name="setting"/> (null when it is): the shape its kind needs, a choice among the
    /// choices, and the log levels' floor. The message names the setting and the rule, never the value: it goes into the log.
    /// </summary>
    public static string? Validate(SettingDefinition setting, string value)
    {
        switch (setting.Kind)
        {
            case SettingKind.Integer:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return $"{setting.Key} must be a whole number.";
                break;
            case SettingKind.Decimal:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return $"{setting.Key} must be a number.";
                break;
            case SettingKind.Boolean:
                if (!bool.TryParse(value, out _)) return $"{setting.Key} must be true or false.";
                break;
            case SettingKind.Choice:
                if (!setting.Choices!.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)) return $"{setting.Key} must be one of {string.Join(", ", setting.Choices!)}.";
                if (IsBelowLogLevelFloor(setting.Key, value)) return $"{setting.Key} must not be below {LogLevelFloor}: the request lines of ASP.NET Core hold the tokens of password reset links.";
                break;
        }
        return null;
    }

    /// <summary>A log level category under <see cref="LogLevelFloorCategory"/> set below <see cref="LogLevelFloor"/>.</summary>
    public static bool IsBelowLogLevelFloor(string key, string value)
    {
        const string prefix = "Logging:LogLevel:";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var category = key[prefix.Length..];
        var floored = category.Equals(LogLevelFloorCategory, StringComparison.OrdinalIgnoreCase)
            || category.StartsWith(LogLevelFloorCategory + ".", StringComparison.OrdinalIgnoreCase);
        return floored && Enum.TryParse<LogLevel>(value.Trim(), ignoreCase: true, out var level) && level < LogLevelFloor;
    }

    /// <summary>The problems of every managed setting that <paramref name="valueOf"/> has a value for (lists are not checked: every string is one).</summary>
    public static IReadOnlyList<string> Problems(Func<string, string?> valueOf)
    {
        var problems = new List<string>();
        foreach (var setting in All)
        {
            if (setting.IsList) continue;
            var value = valueOf(setting.Key);
            if (value is null) continue;
            if (Validate(setting, value) is { } problem) problems.Add(problem);
        }
        return problems;
    }
}
