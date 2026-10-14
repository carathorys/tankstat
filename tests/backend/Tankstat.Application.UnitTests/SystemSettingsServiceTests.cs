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

        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.GetAsync(null, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.SystemSettings.RestartAsync(null, default));
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

        var view = await w.SystemSettings.GetAsync(null, default);

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
        ], null, default);

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
        Assert.Equal(1, w.SystemConfiguration.Reloads); // taken in at once after the write
        // A default applies at once (its readers read it per request), so nothing of it waits; the sign-in and the mail still wait for a restart.
        Assert.Equal(("USD", false, SettingSource.File), (Setting(view, "Defaults:Currency").Value, Setting(view, "Defaults:Currency").RestartPending, Setting(view, "Defaults:Currency").Source));
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
    public async Task ASaveTheRunningConfigurationCannotTakeIn_IsStillASave_AndTheViewSaysWhatWaits()
    {
        var w = AdminWorld(w => w.SystemConfiguration.ApplyFailure = new InvalidOperationException("a reader refused the reload"));

        var view = await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "USD")], null, default);

        Assert.Equal("USD", w.SettingsFile.Values["Defaults:Currency"]); // written, checked: not undone, and not answered as refused
        Assert.Equal(("USD", true), (Setting(view, "Defaults:Currency").Value, Setting(view, "Defaults:Currency").RestartPending)); // not taken in, so it waits
        var warning = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Warning);
        Assert.IsType<InvalidOperationException>(warning.Exception);
        Assert.Equal("none", Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Information).Values["Live"]);
    }

    [Fact]
    public async Task ASecret_IsKeptByABlank_AndRemovedByClear()
    {
        var w = AdminWorld(w => w.SettingsFile.Values["Smtp:Password"] = "old-zzzz");

        await w.SystemSettings.UpdateAsync([Set("Smtp:Password", ""), Set("Smtp:Host", "mail.x.co")], null, default);
        Assert.Equal("old-zzzz", w.SettingsFile.Values["Smtp:Password"]);

        await w.SystemSettings.UpdateAsync([new SettingChange("Smtp:Password", null, Clear: true)], null, default);
        Assert.False(w.SettingsFile.Values.ContainsKey("Smtp:Password"));
        Assert.False(Setting(await w.SystemSettings.GetAsync(null, default), "Smtp:Password").IsSet);

        await w.SystemSettings.UpdateAsync([Set("Smtp:Password", "new-zzzz")], null, default);
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

        await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "  "), Set("Auth:AdminEmails", null), new SettingChange("Auth:Oidc:Scopes", "ignored", Clear: true)], null, default);

        Assert.Empty(w.SettingsFile.Values);
    }

    [Fact]
    public async Task WhatItCannotTake_IsRefusedByKey_AndNothingIsWritten()
    {
        var w = AdminWorld(w => w.SystemConfiguration.Environment["Auth:Mode"] = "Standalone");

        var unknown = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Database:Provider", "PostgreSql")], null, default));
        Assert.Equal(("settings.unknownKey", "Database:Provider"), (unknown.Key, unknown.Args["key"]));

        var readOnly = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("auth:mode", "Oidc")], null, default));
        Assert.Equal(("settings.readOnly", "Auth:Mode"), (readOnly.Key, readOnly.Args["key"]));

        var shape = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF"), Set("Smtp:Port", "zzzz")], null, default));
        Assert.Equal("settings.invalid", shape.Key);
        Assert.Contains((IEnumerable<string>)shape.Args["problems"]!, p => p.StartsWith("Smtp:Port"));
        Assert.DoesNotContain("zzzz", shape.Message);

        var rule = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Notifications:MaxPerHour", "0")], null, default));
        Assert.Equal("settings.invalid", rule.Key);
        Assert.Contains((IEnumerable<string>)rule.Args["problems"]!, p => p.Contains("Notifications:MaxPerHour"));

        var crossGroup = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Smtp:Host", "mail.x.co"), Set("Smtp:From", "t@x.co")], null, default));
        Assert.Contains((IEnumerable<string>)crossGroup.Args["problems"]!, p => p.Contains("Auth:PublicUrl")); // Standalone with mail needs the public address

        Assert.Equal(0, w.SettingsFile.Writes);
        Assert.Empty(w.Log.From<SystemSettingsService>().Where(e => e.Level >= LogLevel.Information));
    }

    [Fact]
    public async Task NoChange_WritesNothing()
    {
        var w = AdminWorld();

        await w.SystemSettings.UpdateAsync([], null, default);

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

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Standalone")], null, default));
        Assert.Equal("settings.standaloneNeedsAdministrator", refused.Key);
        Assert.Equal(0, w.SettingsFile.Writes);

        w.SystemConfiguration.Environment["Auth:Standalone:AdminEmail"] = "root@x.co";
        w.SystemConfiguration.Environment["Auth:Standalone:AdminPassword"] = "initial-password-1";
        await w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Standalone")], null, default); // the first start creates the administrator from these
        Assert.Equal("Standalone", w.SettingsFile.Values["Auth:Mode"]);
    }

    [Theory]
    [InlineData("root@x.co", "changeme", "password")] // under the policy's 10 characters: the first start would throw password.tooShort
    [InlineData("root", "initial-password-1", "email")] // not an address
    [InlineData("taken@x.co", "initial-password-1", "taken")] // a local account (here a user, as a disabled administrator would) holds it: the new one could not be added
    public async Task SwitchingToStandalone_NeedsCredentialsTheFirstStartCanUse(string email, string password, string reason)
    {
        var w = new World(AuthMode.Oidc, configure: o => o.Mode = AuthMode.Oidc);
        var external = User.CreateExternal(UserProvider.Oidc, "sub-1", "owner@x.co", "Owner", isAdmin: true);
        w.Users.Items.Add(external);
        w.Current.SignInAs(external);
        w.AddUser("taken@x.co");
        w.SystemConfiguration.FileAtStart["Auth:Mode"] = "Oidc";
        w.SettingsFile.Values["Auth:Mode"] = "Oidc";
        w.SystemConfiguration.Environment["Auth:Standalone:AdminEmail"] = email;
        w.SystemConfiguration.Environment["Auth:Standalone:AdminPassword"] = password;

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Standalone")], null, default));

        Assert.Equal(("settings.standaloneNeedsAdministrator", reason), (refused.Key, refused.Args["reason"]));
        Assert.Equal(0, w.SettingsFile.Writes);
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
        var unreachable = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync(toOidc, null, default));
        Assert.Equal(("settings.oidcUnreachable", "https://id.example.com/", "unreachable"), (unreachable.Key, unreachable.Args["authority"], unreachable.Args["reason"]));

        w.Discovery.Answer = OidcDiscoveryResult.Found("https://login.example.com");
        var mismatch = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync(toOidc, null, default));
        Assert.Equal(("settings.oidcIssuerMismatch", "https://login.example.com"), (mismatch.Key, mismatch.Args["issuer"]));
        Assert.Equal(0, w.SettingsFile.Writes);

        w.Discovery.Answer = OidcDiscoveryResult.Found("https://id.example.com"); // the same place without the trailing slash
        await w.SystemSettings.UpdateAsync(toOidc, null, default);
        Assert.Equal(["https://id.example.com/", "https://id.example.com/", "https://id.example.com/"], w.Discovery.Probed);
        Assert.Equal("Oidc", w.SettingsFile.Values["Auth:Mode"]);

        foreach (var (key, value) in w.SettingsFile.Values) w.SystemConfiguration.FileAtStart[key] = value; // restarted: the file runs, in OIDC mode
        await w.SystemSettings.UpdateAsync([Set("Smtp:Host", "mail.x.co"), Set("Smtp:From", "t@x.co"), Set("Auth:PublicUrl", "https://tank.test")], null, default);
        Assert.Equal(3, w.Discovery.Probed.Count); // the mode and the authority the next start takes up run already: nothing is asked
        Assert.False(w.Log.Mentions("s-zzzz"));
    }

    [Fact]
    public async Task ASaveThatLeadsTheNextStartIntoOidc_ProbesTheAuthority_WhateverItChanged()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.Shipped["Auth:Mode"] = "Standalone";
            w.SystemConfiguration.File = SettingsFileStatus.LeftOut("/data/settings.json", ["Notifications:MaxPerHour must be at least 1."]);
            w.SettingsFile.Values["Auth:Mode"] = "Oidc"; // left out at start with the rest of the file: the app runs Standalone
            w.SettingsFile.Values["Auth:Oidc:Authority"] = "https://id.example.cmo";
            w.SettingsFile.Values["Auth:Oidc:ClientId"] = "tankstat";
            w.SettingsFile.Values["Auth:Oidc:ClientSecret"] = "s-zzzz";
            w.SettingsFile.Values["Notifications:MaxPerHour"] = "0";
        });
        w.Discovery.Answer = OidcDiscoveryResult.Failed("unreachable");

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Notifications:MaxPerHour", "30")], null, default));

        Assert.Equal("settings.oidcUnreachable", refused.Key); // fixing the one mistake would have the next start sign in through a mistyped authority
        Assert.Equal(["https://id.example.cmo"], w.Discovery.Probed);
        Assert.Equal(0, w.SettingsFile.Writes);
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
            w.SettingsFile.FailRead = new InvalidDataException("Failed to load configuration from file."); // what the JSON provider throws for a file it cannot parse
        });

        var view = await w.SystemSettings.GetAsync(null, default);
        Assert.True(view.File.Ignored);
        Assert.False(Setting(view, "Defaults:Currency").IsSet);
        Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Warning);

        await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default);
        Assert.Equal(["Defaults:Currency"], w.SettingsFile.Values.Keys);
    }

    [Fact]
    public async Task AFileNotLeftOut_ThatCannotBeReadNow_IsNotWrittenOver()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.File = SettingsFileStatus.Used("/data/settings.json", exists: true); // what it held runs, its sign-in mode too
            w.SettingsFile.FailRead = new InvalidDataException("Failed to load configuration from file."); // broken by hand since the start
        });

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default));

        Assert.Equal("settings.invalid", refused.Key); // a save would have kept only the currency, and the next start would run without the file's Auth:Mode
        Assert.Equal(0, w.SettingsFile.Writes);
        Assert.False(Setting(await w.SystemSettings.GetAsync(null, default), "Defaults:Currency").IsSet); // the page still opens
    }

    [Fact]
    public async Task AFileTheAppMayNotOpen_CountsAsEmptyToo()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.File = SettingsFileStatus.LeftOut("/data/settings.json", ["The file cannot be read."]);
            w.SettingsFile.FailRead = new UnauthorizedAccessException("Access to the path is denied."); // EACCES, which is no IOException
        });

        var view = await w.SystemSettings.GetAsync(null, default);

        Assert.True(view.File.Ignored);
        Assert.False(Setting(view, "Defaults:Currency").IsSet);
        Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ASecret_IsStoredAsTyped()
    {
        var w = AdminWorld();

        await w.SystemSettings.UpdateAsync([Set("Smtp:Password", " pass word "), Set("Defaults:Currency", " HUF ")], null, default);

        Assert.Equal((" pass word ", "HUF"), (w.SettingsFile.Values["Smtp:Password"], w.SettingsFile.Values["Defaults:Currency"])); // only what is not a secret is trimmed
    }

    [Fact]
    public async Task AMistakeOfTheEnvironmentsOwn_TheAppRunsWith_HoldsNoSaveBack()
    {
        var w = AdminWorld(w => w.SystemConfiguration.Environment["Smtp:Security"] = "Tls"); // no name of MailKit's: the sender runs it as Auto, so the app starts

        await w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default);
        Assert.Equal("HUF", w.SettingsFile.Values["Defaults:Currency"]);

        var added = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Notifications:MaxPerHour", "0")], null, default));
        Assert.Contains((IEnumerable<string>)added.Args["problems"]!, p => p.Contains("Notifications:MaxPerHour")); // what the file adds still counts
    }

    [Fact]
    public async Task AnEnvironmentThatLeansOnTheFile_StillNeedsWhatTheFileGives()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.Environment["Auth:Mode"] = "Oidc"; // the authority only the file holds
            foreach (var (key, value) in new Dictionary<string, string?> { ["Auth:Oidc:Authority"] = "https://id.example.com", ["Auth:Oidc:ClientId"] = "tankstat", ["Auth:Oidc:ClientSecret"] = "s-zzzz" })
            {
                w.SystemConfiguration.FileAtStart[key] = value;
                w.SettingsFile.Values[key] = value;
            }
        });

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([new SettingChange("Auth:Oidc:Authority", null, Clear: true)], null, default));

        Assert.Contains((IEnumerable<string>)refused.Args["problems"]!, p => p.Contains("Auth:Oidc:Authority")); // the environment lacks it too, but the host would not start
        Assert.Equal(0, w.SettingsFile.Writes);
    }

    [Fact]
    public async Task TwoSavesAtOnce_BothLand()
    {
        var w = AdminWorld(w =>
        {
            w.SystemConfiguration.FileAtStart["Auth:Mode"] = "Standalone";
            w.SettingsFile.Values["Auth:Mode"] = "Standalone";
        });
        w.Discovery.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); // an authority that takes its time

        var toOidc = w.SystemSettings.UpdateAsync([Set("Auth:Mode", "Oidc"), Set("Auth:Oidc:Authority", "https://id.example.com"), Set("Auth:Oidc:ClientId", "tankstat"), Set("Auth:Oidc:ClientSecret", "s-zzzz")], null, default);
        await w.Discovery.Asked.Task; // the first save read the file and waits for the authority
        var currency = w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default); // another group's form, meanwhile
        w.Discovery.Hold.SetResult();
        await Task.WhenAll(toOidc, currency);

        Assert.Equal(("Oidc", "HUF"), (w.SettingsFile.Values["Auth:Mode"], w.SettingsFile.Values["Defaults:Currency"]));
    }

    [Fact]
    public async Task AFileThatCannotBeWritten_IsAKeyedError_WithTheExceptionInTheLog()
    {
        var w = AdminWorld(w => w.SettingsFile.FailWrite = new IOException("disk full"));

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.UpdateAsync([Set("Defaults:Currency", "HUF")], null, default));

        Assert.Equal("settings.fileNotWritable", refused.Key);
        var warning = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Warning); // handled, so no Error
        Assert.IsType<IOException>(warning.Exception);
        Assert.DoesNotContain(w.Log.From<SystemSettingsService>(), e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Restart_IsLoggedByAdministrator_AndAsked()
    {
        var w = AdminWorld();

        await w.SystemSettings.RestartAsync(null, default);

        Assert.Equal(1, w.Restart.Count);
        var line = Assert.Single(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Information);
        Assert.Equal(w.Current.Principal!.Id, line.Values["AdminId"]);
    }

    [Fact]
    public async Task ARestartTheNextStartWouldRefuse_IsRefused_ForAStartThatFailsEndsTheProcess()
    {
        var w = AdminWorld();
        w.SystemConfiguration.NextStartRefusal = ["Notifications:MaxPerHour must be at least 1."]; // a file hand-edited since the start

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.RestartAsync(null, default));

        Assert.Equal("settings.restartWouldFail", refused.Key);
        Assert.Contains("Notifications:MaxPerHour", refused.Message);
        Assert.Equal(0, w.Restart.Count);
        Assert.DoesNotContain(w.Log.From<SystemSettingsService>(), e => e.Level == LogLevel.Information); // no restart to log
    }

    [Fact]
    public async Task ARestartIntoStandalone_WithNoAdministratorItCouldCreate_IsRefused()
    {
        var w = new World(AuthMode.Oidc, configure: o => o.Mode = AuthMode.Oidc);
        var external = User.CreateExternal(UserProvider.Oidc, "sub-1", "owner@x.co", "Owner", isAdmin: true);
        w.Users.Items.Add(external);
        w.Current.SignInAs(external); // no local administrator exists
        w.SystemConfiguration.FileAtNextStart = new(StringComparer.OrdinalIgnoreCase) { ["Auth:Mode"] = "Standalone" }; // written by hand since

        var refused = await Assert.ThrowsAsync<DomainException>(() => w.SystemSettings.RestartAsync(null, default));
        Assert.Equal("settings.standaloneNeedsAdministrator", refused.Key);
        Assert.Equal(0, w.Restart.Count);

        w.SystemConfiguration.Environment["Auth:Standalone:AdminEmail"] = "root@x.co";
        w.SystemConfiguration.Environment["Auth:Standalone:AdminPassword"] = "initial-password-1";
        await w.SystemSettings.RestartAsync(null, default); // the next start creates the administrator from these
        Assert.Equal(1, w.Restart.Count);
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
