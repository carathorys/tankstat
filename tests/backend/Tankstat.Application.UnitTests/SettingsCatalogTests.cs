using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Recognition;
using Tankstat.Application.Sync;
using Tankstat.Application.SystemSettings;
using Tankstat.Application.Vehicles;

namespace Tankstat.Application.UnitTests;

public class SettingsCatalogTests
{
    /// <summary>The sections the page manages, as the options classes bind them.</summary>
    private static readonly (Type Options, string Section)[] Sections =
    [
        (typeof(AuthOptions), AuthOptions.SectionName),
        (typeof(SmtpOptions), SmtpOptions.SectionName),
        (typeof(VehicleDefaultsOptions), VehicleDefaultsOptions.SectionName),
        (typeof(NotificationOptions), NotificationOptions.SectionName),
        (typeof(SyncOptions), SyncOptions.SectionName),
        (typeof(RecognitionOptions), RecognitionOptions.SectionName),
    ];

    [Fact]
    public void EveryPropertyOfTheManagedSections_IsInTheCatalog_OrLeftOutOnPurpose()
    {
        var expected = Sections.SelectMany(s => Keys(s.Options, s.Section)).ToDictionary(k => k.Key, k => k, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, kind, choices) in expected.Values)
        {
            if (SettingsCatalog.LeftOut.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                Assert.Null(SettingsCatalog.Find(key));
                continue;
            }
            var definition = SettingsCatalog.Find(key);
            Assert.True(definition is not null, $"{key} is neither in the catalog nor left out on purpose");
            Assert.Equal(key, definition!.Key); // the catalog spells the key as the options class does
            if (kind is SettingKind.Text) Assert.Contains(definition.Kind, new[] { SettingKind.Text, SettingKind.Multiline, SettingKind.Secret, SettingKind.Choice });
            else Assert.Equal(kind, definition.Kind);
            if (choices is not null) Assert.Equal(choices, definition.Choices);
        }
        foreach (var definition in SettingsCatalog.All.Where(d => d.Group != SettingsGroup.Logging))
            Assert.True(expected.ContainsKey(definition.Key), $"{definition.Key} is in the catalog but no options class binds it");
    }

    [Fact]
    public void ChoicesAreThere_ExactlyForChoices_AndSecretsAreTheThree()
    {
        Assert.All(SettingsCatalog.All, d => Assert.Equal(d.Kind == SettingKind.Choice, d.Choices is { Count: > 0 }));
        Assert.All(SettingsCatalog.All.Where(d => d.AlsoAccepted is not null), d =>
        {
            Assert.Equal(SettingKind.Choice, d.Kind);
            Assert.Empty(d.AlsoAccepted!.Intersect(d.Choices!, StringComparer.OrdinalIgnoreCase)); // accepted besides the choices, never offered twice
        });
        Assert.Equal(
            ["Auth:Oidc:ClientSecret", "Smtp:Password", "Recognition:OpenAiCompatible:ApiKey"],
            SettingsCatalog.All.Where(d => d.IsSecret).Select(d => d.Key));
        Assert.Equal(SettingsCatalog.Groups, SettingsCatalog.All.Select(d => d.Group).Distinct()); // the page's order
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(d => d.Key.ToLowerInvariant()).Distinct().Count());
    }

    [Theory]
    [InlineData("Smtp:Port", "587", null)]
    [InlineData("Smtp:Port", " 587 ", null)]
    [InlineData("Smtp:Port", "5.5", "whole number")]
    [InlineData("Smtp:Port", "zzzz", "whole number")]
    [InlineData("Recognition:MinConfidence", "0.5", null)]
    [InlineData("Recognition:MinConfidence", "zzzz", "number")]
    [InlineData("Recognition:MinConfidence", "NaN", "number")]
    [InlineData("Auth:Standalone:AllowAdminSetPassword", "true", null)]
    [InlineData("Auth:Standalone:AllowAdminSetPassword", "False", null)]
    [InlineData("Auth:Standalone:AllowAdminSetPassword", "yes", "true or false")]
    [InlineData("Auth:Mode", "standalone", null)]
    [InlineData("Auth:Mode", "Kerberos", "None, Standalone, Oidc, ProxyHeader")]
    [InlineData("Smtp:Security", "starttls", null)]
    [InlineData("Smtp:Security", "SslOnConnect", null)]
    [InlineData("Smtp:Security", "ssl", null)] // the old spelling: accepted (the sender runs it as Auto), never offered
    [InlineData("Smtp:Security", "zzzz", "Auto, None, StartTls, StartTlsWhenAvailable, SslOnConnect")]
    [InlineData("Logging:LogLevel:Default", "Trace", null)]
    [InlineData("Logging:LogLevel:Default", "Verbose", "Trace, Debug")]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore", "Warning", null)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore", "None", null)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore", "Information", "must not be below Warning")]
    [InlineData("Auth:PublicUrl", "anything at all", null)]
    public void Validate_ChecksTheShapeOfAValue_AndNamesTheSettingAndTheRule(string key, string value, string? refusal)
    {
        var problem = SettingsCatalog.Validate(SettingsCatalog.Find(key)!, value);

        if (refusal is null) Assert.Null(problem);
        else
        {
            Assert.StartsWith(key, problem);
            Assert.Contains(refusal, problem);
            Assert.DoesNotContain(value.Trim(), problem); // the message goes to the log: the rule, never the value
        }
    }

    [Theory]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore", "Debug", true)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore.Hosting", "Information", true)] // a category below it
    [InlineData("Logging:LogLevel:microsoft.aspnetcore", "trace", true)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore", "Warning", false)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCoreX", "Debug", false)] // a common prefix is not the category
    [InlineData("Logging:LogLevel:Microsoft", "Debug", false)]
    [InlineData("Logging:LogLevel:Default", "Trace", false)]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore", "Debug", false)] // not the LogLevel section
    public void TheAspNetCoreCategories_NeverGoBelowWarning(string key, string value, bool below) =>
        Assert.Equal(below, SettingsCatalog.IsBelowLogLevelFloor(key, value));

    [Fact]
    public void Problems_NamesEveryWrongShape_AndSkipsListsUnsetSettingsPhotoReadingAndTheFloor()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Smtp:Port"] = "zzzz",
            ["Notifications:MaxPerHour"] = "0", // the shape is right; the range is the validator's business
            ["Auth:AdminEmails"] = "not a list, but lists are never checked here",
            ["Auth:Mode"] = "Kerberos",
            ["Recognition:MinConfidence"] = "zzzz", // photo reading never stops the app: RecognitionSetup turns it off instead
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Information", // the floor is a rule for what the page writes, not for the start
        };

        var problems = SettingsCatalog.Problems(key => values.GetValueOrDefault(key));

        Assert.Equal(["Auth:Mode", "Smtp:Port"], problems.Select(p => p.Split(' ')[0]));
        Assert.NotNull(SettingsCatalog.Validate(SettingsCatalog.Find("Logging:LogLevel:Microsoft.AspNetCore")!, "Information")); // the page still refuses it
        Assert.NotNull(SettingsCatalog.Validate(SettingsCatalog.Find("Recognition:MinConfidence")!, "zzzz"));
    }

    private static IEnumerable<(string Key, SettingKind Kind, string[]? Choices)> Keys(Type type, string prefix)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var key = $"{prefix}:{property.Name}";
            var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (t == typeof(string)) yield return (key, SettingKind.Text, null);
            else if (t == typeof(int) || t == typeof(long)) yield return (key, SettingKind.Integer, null);
            else if (t == typeof(double)) yield return (key, SettingKind.Decimal, null);
            else if (t == typeof(bool)) yield return (key, SettingKind.Boolean, null);
            else if (t.IsEnum) yield return (key, SettingKind.Choice, Enum.GetNames(t));
            else if (typeof(IEnumerable).IsAssignableFrom(t)) yield return (key, SettingKind.List, null);
            else foreach (var nested in Keys(t, key)) yield return nested;
        }
    }
}

public class SettingsFileNoticeTests
{
    [Fact]
    public void AFileLeftOut_IsAPublicWarning_ThatNamesNoSetting()
    {
        var status = SettingsFileStatus.LeftOut("/data/settings.json", ["Smtp:Port must be a whole number."]);

        var notices = new NoticeService(new AuthOptions { Mode = AuthMode.None }.Create(), status).GetNotices();

        Assert.Equal(["AUTH_DISABLED", "SETTINGS_FILE_IGNORED"], notices.Select(n => n.Code)); // the unsafe warning stays first
        var notice = notices[1];
        Assert.Equal(NoticeSeverity.Warning, notice.Severity);
        Assert.DoesNotContain("Smtp", notice.Message);
        Assert.DoesNotContain("/data", notice.Message);
        Assert.Equal(["SETTINGS_FILE_IGNORED"], new NoticeService(new AuthOptions { Mode = AuthMode.Standalone }.Create(), status).GetNotices().Select(n => n.Code));
    }

    [Fact]
    public void AFileInUse_OrNone_OrNoDecision_AddsNoNotice()
    {
        var options = new AuthOptions { Mode = AuthMode.Standalone }.Create();

        Assert.Empty(new NoticeService(options, SettingsFileStatus.Used("/data/settings.json", exists: true)).GetNotices());
        Assert.Empty(new NoticeService(options, SettingsFileStatus.Used("/data/settings.json", exists: false)).GetNotices());
        Assert.Empty(new NoticeService(options).GetNotices());
    }

    [Fact]
    public void TheLogLevelFloor_IsTheOneTheConfigurationDocumentationPromises() => Assert.Equal(LogLevel.Warning, SettingsCatalog.LogLevelFloor);
}
