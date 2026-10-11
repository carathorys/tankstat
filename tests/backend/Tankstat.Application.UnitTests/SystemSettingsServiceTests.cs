using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.UnitTests;

public class SystemSettingsServiceTests
{
    private static World AdminWorld(Action<World>? setup = null)
    {
        var w = new World();
        setup?.Invoke(w);
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        return w;
    }

    private static SettingView Setting(SystemSettingsView view, string key) =>
        view.Groups.SelectMany(g => g.Settings).Single(s => s.Definition.Key == key);

    private static SettingChange Set(string key, string? value) => new(key, value);

    [Fact]
    public async Task OnlyAdministrators_SeeOrChangeSystemSettings()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.GetAsync(default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.RestartAsync(default));
        Assert.Equal((0, 0), (w.SettingsFile.Writes, w.Restart.Count));
    }

    [Fact]
    public async Task Get_ShowsEverySettingOfTheCatalog_WithItsSourceAndWhetherThePageMayChangeIt()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.Environment["Auth:Mode"] = "Oidc";
            w.SystemConfiguration.Environment["Auth:AdminEmails:0"] = "owner@x.co";
            w.SystemConfiguration.Shipped["Logging:LogLevel:Default"] = "Information";
            w.SystemConfiguration.FileAtStart["Defaults:Currency"] = "HUF";
            w.SystemConfiguration.FileAtStart["Smtp:Password"] = "hunter2-zzzz";
            foreach (var (k, v) in w.SystemConfiguration.FileAtStart) w.SettingsFile.Values[k] = v;
        });

        var view = await w.SystemSettings.GetAsync(default);

        Assert.Equal(SettingsCatalog.Groups, view.Groups.Select(g => g.Group));
        Assert.Equal(SettingsCatalog.All.Select(d => d.Key), view.Groups.SelectMany(g => g.Settings).Select(s => s.Definition.Key));
        var mode = Setting(view, "Auth:Mode");
        Assert.Equal((SettingSource.Environment, false, "Oidc", true), (mode.Source, mode.Editable, mode.Value, mode.IsSet));
        Assert.Equal((SettingSource.Environment, false, "owner@x.co"), (Setting(view, "Auth:AdminEmails").Source, Setting(view, "Auth:AdminEmails").Editable, Setting(view, "Auth:AdminEmails").Value));
        Assert.Equal((SettingSource.Shipped, true, "Information"), (Setting(view, SettingsCatalog.LoggingDefault).Source, Setting(view, SettingsCatalog.LoggingDefault).Editable, Setting(view, SettingsCatalog.LoggingDefault).Value));
        Assert.Equal((SettingSource.File, true, "HUF", false), (Setting(view, "Defaults:Currency").Source, Setting(view, "Defaults:Currency").Editable, Setting(view, "Defaults:Currency").Value, Setting(view, "Defaults:Currency").RestartPending));
        var password = Setting(view, "Smtp:Password");
        Assert.Equal((null, true, SettingSource.File), (password.Value, password.IsSet, password.Source)); // set, never shown
        Assert.Equal((SettingSource.Default, true, false, null), (Setting(view, "Smtp:Host").Source, Setting(view, "Smtp:Host").Editable, Setting(view, "Smtp:Host").IsSet, Setting(view, "Smtp:Host").Value));
        Assert.False(view.RestartPending);
        Assert.Equal((w.SystemConfiguration.File.Path, false, false), (view.File.Path, view.File.Ignored, view.File.Exists));
    }

    [Fact]
    public async Task Update_WritesTheFileWhole_KeepsWhatItDoesNotManage_AndSaysWhatWaitsForARestart()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.FileAtStart["Defaults:Currency"] = "HUF";
            w.SettingsFile.Values["Defaults:Currency"] = "HUF";
            w.SettingsFile.Values["Backups:Keep"] = "5"; // a setting of a later version, or written by hand: untouched
        });

        var view = await w.SystemSettings.UpdateAsync(
        [
            Set("Defaults:Currency", "USD"),
            Set("Auth:AdminEmails", "a@x.co\n b@x.co \n\n"),
            Set("Smtp:Port", " 2525 "),
            Set("Smtp:Password", "hunter2-zzzz"),
            Set("Smtp:Host", ""),
        ], default);

        Assert.Equal(1, w.SettingsFile.Writes);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Backups:Keep"] = "5",
                ["Defaults:Currency"] = "USD",
                ["Auth:AdminEmails:0"] = "a@x.co",
                ["Auth:AdminEmails:1"] = "b@x.co",
                ["Smtp:Port"] = "2525",
                ["Smtp:Password"] = "hunter2-zzzz",
            }.OrderBy(p => p.Key),
            w.SettingsFile.Values.OrderBy(p => p.Key));
        Assert.Equal(("USD", true, SettingSource.File), (Setting(view, "Defaults:Currency").Value, Setting(view, "Defaults:Currency").RestartPending, Setting(view, "Defaults:Currency").Source));
        Assert.Equal(("a@x.co\nb@x.co", true, SettingSource.Default), (Setting(view, "Auth:AdminEmails").Value, Setting(view, "Auth:AdminEmails").RestartPending, Setting(view, "Auth:AdminEmails").Source)); // not in the running configuration yet
        Assert.Equal((null, true, true), (Setting(view, "Smtp:Password").Value, Setting(view, "Smtp:Password").IsSet, Setting(view, "Smtp:Password").RestartPending));
        Assert.True(view.RestartPending);

        var line = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Information);
        Assert.Equal(w.Current.Principal!.Id, line.Values["AdminId"]);
        var keys = (string)line.Values["Keys"]!;
        Assert.Contains("Smtp:Password", keys);
        Assert.Contains("Defaults:Currency", keys);
        Assert.DoesNotContain("USD", keys);
        Assert.False(w.Log.Mentions("hunter2-zzzz"));
        Assert.False(w.Log.Mentions("a@x.co"));
    }

    [Fact]
    public async Task ASecret_IsKeptByABlank_AndRemovedByClear()
    {
        var w = AdminWorld(w => w.SettingsFile.Values["Smtp:Password"] = "old-zzzz");

        await w.SystemSettings.UpdateAsync([Set("Smtp:Password", ""), Set("Smtp:Host", "mail.x.co")], default);
        Assert.Equal("old-zzzz", w.SettingsFile.Values["Smtp:Password"]);

        await w.SystemSettings.UpdateAsync([new SettingChange("Smtp:Password", null, Clear: true)], default);
        Assert.False(w.SettingsFile.Values.ContainsKey("Smtp:Password"));
        Assert.False(Setting(await w.SystemSettings.GetAsync(default), "Smtp:Password").IsSet);

        await w.SystemSettings.UpdateAsync([Set("Smtp:Password", "new-zzzz")], default);
        Assert.Equal("new-zzzz", w.SettingsFile.Values["Smtp:Password"]);
    }

    [Fact]
    public async Task ANonSecret_IsClearedByABlank_OrByClear_AndAListByEither()
    {
        var w = AdminWorld(w =>
        {
            w.SettingsFile.Values["Defaults:Currency"] = "HUF";
            w.SettingsFile.Values["Auth:AdminEmails:0"] = "a@x.co";
            w.SettingsFile.Values["Auth:Oidc:Scopes:0"] = "openid";
        });

        await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "  "), Set("Auth:AdminEmails", null), new SettingChange("Auth:Oidc:Scopes", "ignored", Clear: true)], default);

        Assert.Empty(w.SettingsFile.Values);
    }

    [Fact]
    public async Task WhatItCannotTake_IsRefusedByKey_AndNothingIsWritten()
    {
        var w = AdminWorld(w => w.SystemConfiguration.Environment["Auth:Mode"] = "Standalone");

        var unknown = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Database:Provider", "PostgreSql")], default));
        Assert.Equal(("settings.unknownKey", "Database:Provider"), (unknown.Key, unknown.Args["key"]));

        var readOnly = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("auth:mode", "Oidc")], default));
        Assert.Equal(("settings.readOnly", "Auth:Mode"), (readOnly.Key, readOnly.Args["key"]));

        var shape = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF"), Set("Smtp:Port", "zzzz")], default));
        Assert.Equal("settings.invalid", shape.Key);
        Assert.Contains((IEnumerable<string>)shape.Args["problems"]!, p => p.StartsWith("Smtp:Port"));
        Assert.DoesNotContain("zzzz", shape.Message);

        var rule = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Notifications:MaxPerHour", "0")], default));
        Assert.Equal("settings.invalid", rule.Key);
        Assert.Contains((IEnumerable<string>)rule.Args["problems"]!, p => p.Contains("Notifications:MaxPerHour"));

        var crossGroup = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Smtp:Host", "mail.x.co"), Set("Smtp:From", "t@x.co")], default));
        Assert.Contains((IEnumerable<string>)crossGroup.Args["problems"]!, p => p.Contains("Auth:PublicUrl")); // Standalone with mail needs the public address

        Assert.Equal(0, w.SettingsFile.Writes);
        Assert.Empty(w.Log.From<SystemSettingsService>().Where(e => e.Level >= LogLevel.Information));
    }

    [Fact]
    public async Task NoChange_WritesNothing()
    {
        var w = AdminWorld();

        await w.SystemSettings.UpdateAsync([], default);

        Assert.Equal(0, w.SettingsFile.Writes);
    }

    [Fact]
    public async Task SwitchingToStandalone_NeedsAnAdministratorToSignInAfterwards()
    {
        var w = new World(AuthMode.Oidc, configure: o => o.Mode = AuthMode.Oidc);
        var external = User.CreateExternal(UserProvider.Oidc, "sub-1", "owner@x.co", "Owner", isAdmin: true);
        w.Users.Items.Add(external);
        w.Current.SignInAs(external); // no local administrator exists
        w.SystemConfiguration.FileAtStart["Auth:Mode"] = "Oidc";
        w.SettingsFile.Values["Auth:Mode"] = "Oidc";

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Standalone")], default));
        Assert.Equal("settings.standaloneNeedsAdministrator", refused.Key);
        Assert.Equal(0, w.SettingsFile.Writes);

        w.SystemConfiguration.Environment["Auth:Standalone:AdminEmail"] = "root@x.co";
        w.SystemConfiguration.Environment["Auth:Standalone:AdminPassword"] = "initial-password-1";
        await w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Standalone")], default); // the first start creates the administrator from these
        Assert.Equal("Standalone", w.SettingsFile.Values["Auth:Mode"]);
    }

    [Fact]
    public async Task SwitchingToOidc_ChecksThatTheAuthorityAnswers_WithThatIssuer()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.FileAtStart["Auth:Mode"] = "Standalone";
            w.SettingsFile.Values["Auth:Mode"] = "Standalone";
        });
        SettingChange[] toOidc = [Set("Auth:Mode", "Oidc"), Set("Auth:Oidc:Authority", "https://id.example.com/"), Set("Auth:Oidc:ClientId", "tankstat"), Set("Auth:Oidc:ClientSecret", "s-zzzz")];

        w.Discovery.Answer = OidcDiscoveryResult.Failed("unreachable");
        var unreachable = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync(toOidc, default));
        Assert.Equal(("settings.oidcUnreachable", "https://id.example.com/", "unreachable"), (unreachable.Key, unreachable.Args["authority"], unreachable.Args["reason"]));

        w.Discovery.Answer = OidcDiscoveryResult.Found("https://login.example.com");
        var mismatch = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync(toOidc, default));
        Assert.Equal(("settings.oidcIssuerMismatch", "https://login.example.com"), (mismatch.Key, mismatch.Args["issuer"]));
        Assert.Equal(0, w.SettingsFile.Writes);

        w.Discovery.Answer = OidcDiscoveryResult.Found("https://id.example.com"); // the same place without the trailing slash
        await w.SystemSettings.UpdateAsync(toOidc, default);
        Assert.Equal(["https://id.example.com/", "https://id.example.com/", "https://id.example.com/"], w.Discovery.Probed);
        Assert.Equal("Oidc", w.SettingsFile.Values["Auth:Mode"]);

        w.SystemConfiguration.FileAtStart["Auth:Mode"] = "Oidc"; // restarted in OIDC mode
        await w.SystemSettings.UpdateAsync([Set("Smtp:Host", "mail.x.co"), Set("Smtp:From", "t@x.co"), Set("Auth:PublicUrl", "https://tank.test")], default);
        Assert.Equal(3, w.Discovery.Probed.Count); // a change that does not touch the mode or the authority asks nothing
        Assert.False(w.Log.Mentions("s-zzzz"));
    }

    [Theory]
    [InlineData("https://id.example.com", "https://id.example.com/", true)]
    [InlineData("https://ID.example.com/realms/x", "https://id.example.com/realms/x", true)]
    [InlineData("https://id.example.com/realms/x", "https://id.example.com/realms/y", false)]
    [InlineData("https://id.example.com", "http://id.example.com", false)]
    [InlineData("not a url", "not a url", true)]
    public void TheIssuer_IsTheAuthority_WhateverTheSlashOrTheCase(string authority, string issuer, bool same) =>
        Assert.Equal(same, SystemSettingsService.SameIssuer(authority, issuer));

    [Fact]
    public async Task AFileThatCannotBeRead_CountsAsEmpty_AndIsReplacedByTheNextSave()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.File = SettingsFileStatus.LeftOut("/data/settings.json", ["The file is not valid JSON."]);
            w.SettingsFile.FailRead = new FormatException("Could not parse the JSON file.");
        });

        var view = await w.SystemSettings.GetAsync(default);
        Assert.True(view.File.Ignored);
        Assert.False(Setting(view, "Defaults:Currency").IsSet);
        Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Warning);

        await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], default);
        Assert.Equal(["Defaults:Currency"], w.SettingsFile.Values.Keys);
    }

    [Fact]
    public async Task AFileThatCannotBeWritten_IsAKeyedError_WithTheExceptionInTheLog()
    {
        var w = AdminWorld(w => w.SettingsFile.FailWrite = new IOException("disk full"));

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], default));

        Assert.Equal("settings.fileNotWritable", refused.Key);
        var error = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Error);
        Assert.IsType<IOException>(error.Exception);
    }

    [Fact]
    public async Task Restart_IsLoggedByAdministrator_AndAsked()
    {
        var w = AdminWorld();

        await w.SystemSettings.RestartAsync(default);

        Assert.Equal(1, w.Restart.Count);
        var line = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Information);
        Assert.Equal(w.Current.Principal!.Id, line.Values["AdminId"]);
    }
}

public class InstanceStartServiceTests
{
    private static UserSession SessionOf(World w, User user) =>
        UserSession.Issue(user.Id, user.SessionVersion, "HASH", "SEALED", "Firefox on Linux", w.Clock.GetUtcNow(), TimeSpan.FromDays(90));

    [Fact]
    public async Task TheFirstStart_RecordsTheMode_AndSignsNobodyOut()
    {
        var w = new World();
        var session = SessionOf(w, w.AddUser("alice@x.co"));
        w.Sessions.Items.Add(session);

        await w.InstanceStart.RecordStartAsync(default);

        Assert.Equal(("Standalone", w.Clock.GetUtcNow()), (w.Instance.Value!.LastStartMode, w.Instance.Value.LastStartedAt));
        Assert.Null(session.RevokedAt);
        Assert.Empty(w.Log.From<InstanceStartService>().Where(e => e.Level >= LogLevel.Information));
    }

    [Fact]
    public async Task AStartInTheSameMode_KeepsEveryDeviceSignedIn()
    {
        var w = new World();
        var session = SessionOf(w, w.AddUser("alice@x.co"));
        w.Sessions.Items.Add(session);
        await w.InstanceStart.RecordStartAsync(default);
        w.Clock.Advance(TimeSpan.FromDays(1));

        await w.InstanceStart.RecordStartAsync(default);

        Assert.Null(session.RevokedAt);
        Assert.Equal((2, w.Clock.GetUtcNow()), (w.Instance.Saves, w.Instance.Value!.LastStartedAt));
    }

    [Fact]
    public async Task AStartInAnotherMode_SignsEveryDeviceOut_AndSaysSo()
    {
        var w = new World();
        var alice = SessionOf(w, w.AddUser("alice@x.co"));
        var bob = SessionOf(w, w.AddUser("bob@x.co"));
        w.Sessions.Items.AddRange([alice, bob]);
        var recorded = Domain.Instance.InstanceState.New();
        recorded.RecordStart("Oidc", w.Clock.GetUtcNow().AddDays(-1));
        w.Instance.Value = recorded;

        await w.InstanceStart.RecordStartAsync(default);

        Assert.All(new[] { alice, bob }, s => Assert.Equal(w.Clock.GetUtcNow(), s.RevokedAt));
        var line = Assert.Single(w.Log.From<InstanceStartService>(), e => e.Level == LogLevel.Information);
        Assert.Equal(("Oidc", "Standalone", 2), (line.Values["Before"], line.Values["After"], line.Values["Count"]));
        Assert.Equal("Standalone", w.Instance.Value!.LastStartMode);
    }

    [Fact]
    public async Task AModeChange_WithNobodySignedIn_IsOnlyNoted()
    {
        var w = new World(AuthMode.None);
        var recorded = Domain.Instance.InstanceState.New();
        recorded.RecordStart("Standalone", w.Clock.GetUtcNow().AddDays(-1));
        w.Instance.Value = recorded;

        await w.InstanceStart.RecordStartAsync(default);

        Assert.Equal("None", w.Instance.Value!.LastStartMode);
        Assert.Empty(w.Log.From<InstanceStartService>().Where(e => e.Level >= LogLevel.Information));
        Assert.Single(w.Log.From<InstanceStartService>(), e => e.Level == LogLevel.Debug);
    }
}
