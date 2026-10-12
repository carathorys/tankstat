using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.SystemSettings;

/// <summary>One change an administrator asks for: a new value, or the setting cleared (back to what the lower sources say). A blank value clears a setting too, except a secret, which a blank keeps.</summary>
public sealed record SettingChange(string Key, string? Value, bool Clear = false);

/// <param name="Value">What the settings file and the sources below it give (the value after a restart); null for a secret.</param>
/// <param name="IsSet">Whether there is a value at all (the one thing said about a secret).</param>
/// <param name="RestartPending">The file's value is not the one running: it takes effect at the next start.</param>
public sealed record SettingView(SettingDefinition Definition, string? Value, bool IsSet, SettingSource Source, bool RestartPending)
{
    /// <summary>Whether the page may change it: only when no environment variable or command-line argument holds it.</summary>
    public bool Editable => IsEditable(Source);

    /// <summary>The one rule for what the page may change, which a save enforces (<c>settings.readOnly</c>) and the view shows.</summary>
    public static bool IsEditable(SettingSource source) => source != SettingSource.Environment;
}

public sealed record SettingsGroupView(SettingsGroup Group, IReadOnlyList<SettingView> Settings);

public sealed record SystemSettingsView(SettingsFileStatus File, IReadOnlyList<SettingsGroupView> Groups)
{
    public bool RestartPending => Groups.Any(g => g.Settings.Any(s => s.RestartPending));
}

/// <summary>
/// The system settings an administrator manages: every setting of the catalog with its source, the changes written into the app's own
/// settings file (after the whole resulting configuration passed the same checks the start runs, and the rules only a running app can
/// check: an OIDC authority that answers, a Standalone mode with an administrator to sign in), and the restart that makes them count.
/// </summary>
public sealed class SystemSettingsService(
    AccessService access, ISystemConfiguration configuration, ISettingsFileStore store, IUserRepository users, IOidcDiscovery discovery,
    IServerRestart restart, ILogger<SystemSettingsService> logger)
{
    /// <summary>
    /// One save at a time in this process: a save reads the file, checks the result (an OIDC authority may take seconds to answer) and writes
    /// it whole, so a second save meanwhile would be written over by the first, its changes lost although it was answered as saved.
    /// </summary>
    private static readonly SemaphoreSlim Saving = new(1, 1);

    public async Task<SystemSettingsView> GetAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await ViewAsync(ct);
    }

    public async Task<SystemSettingsView> UpdateAsync(IReadOnlyList<SettingChange> changes, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var definitions = new Dictionary<string, SettingDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            var definition = SettingsCatalog.Find(change.Key) ?? throw new DomainException("settings.unknownKey", $"{change.Key} is not a setting the page manages.", new { Key = change.Key });
            if (!SettingView.IsEditable(configuration.SourceOf(definition.Key)))
                throw new DomainException("settings.readOnly", $"{definition.Key} is set by the environment, which wins over the settings file.", new { Key = definition.Key });
            definitions[definition.Key] = definition;
        }
        if (definitions.Count == 0) return await ViewAsync(ct);

        var applied = false;
        await Saving.WaitAsync(ct);
        try
        {
            var current = await ReadFileAsync(ct);
            // A file the start left out (not JSON, unreadable) is replaced by a save, the previous one kept as .bak. One that is not left out but
            // cannot be read now (edited by hand since) is not: what it holds may run, the sign-in mode too, and a save would drop all of it.
            if (current is null && !configuration.File.Ignored)
                throw Invalid(["The settings file cannot be read, so a save would replace everything in it, the sign-in mode too. Fix the file by hand, then save again."]);
            var values = new Dictionary<string, string?>(current ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            foreach (var change in changes) Apply(definitions[change.Key], change, values, problems);
            if (problems.Count > 0) throw Invalid(problems);

            var candidate = configuration.With(values);
            try
            {
                problems.AddRange(configuration.Check(candidate));
                if (problems.Count > 0) throw Invalid(problems);
                await CheckRulesOnlyARunningAppKnowsAsync(candidate, ct);
            }
            finally
            {
                (candidate as IDisposable)?.Dispose();
            }

            try
            {
                await store.WriteAsync(values, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "The settings file {Path} could not be written", store.Path);
                throw new DomainException("settings.fileNotWritable", "The settings file could not be written; see the server log.", new { Path = store.Path });
            }
            // Checked as a whole above, so it may run: the settings that apply at once take effect now, the others wait for a restart. The
            // file is written by now, so a failure to take it in does not make the save a refusal: the view says what runs and what waits.
            try
            {
                configuration.ApplySavedFile();
                applied = true;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "The settings file {Path} was saved, but the running configuration could not take it in completely", store.Path);
            }
        }
        finally
        {
            Saving.Release();
        }
        List<string> live = applied ? [.. definitions.Values.Where(d => d.AppliesAtOnce).Select(d => d.Key)] : [];
        logger.LogInformation("Administrator {AdminId} changed system settings {Keys}; in effect at once: {Live}", admin.Id, string.Join(", ", definitions.Keys), live.Count == 0 ? "none" : string.Join(", ", live));
        return await ViewAsync(ct);
    }

    /// <summary>
    /// Restarts the server in its process, unless the next start would fail for what can be told now: a start that fails ends the process (the
    /// next host is built in it), and a container that exited stays stopped. So a settings file the next start would refuse (hand-edited
    /// since, say) and a switch to Standalone that could not create its first administrator refuse the restart instead; what only the start
    /// finds (a database out of reach) still stops it.
    /// </summary>
    public async Task RestartAsync(CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        using (var next = configuration.PrepareNextStart())
        {
            if (next.Configuration is null)
                throw new DomainException("settings.restartWouldFail",
                    $"The server would not start again with the settings file as it is ({string.Join(" ", next.Refusal)}), so it is not restarted: fix the file, or set Auth__Mode in the environment.",
                    new { Problems = next.Refusal });
            await RequireAdministratorAsync(next.Configuration, ct);
        }
        logger.LogInformation("Administrator {AdminId} restarted the server", admin.Id);
        restart.Restart();
    }

    private static void Apply(SettingDefinition definition, SettingChange change, Dictionary<string, string?> values, List<string> problems)
    {
        var value = change.Value?.Trim();
        var blank = string.IsNullOrEmpty(value);
        if (definition.IsSecret && blank && !change.Clear) return; // the page never shows a secret, so an empty field means "keep it"
        if (definition.IsList)
        {
            foreach (var key in values.Keys.Where(k => k.StartsWith(definition.Key + ":", StringComparison.OrdinalIgnoreCase)).ToList()) values.Remove(key);
            if (change.Clear || blank) return;
            var items = value!.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < items.Length; i++) values[$"{definition.Key}:{i}"] = items[i];
            return;
        }
        values.Remove(definition.Key);
        if (change.Clear || blank) return;
        if (SettingsCatalog.Validate(definition, value!) is { } problem) problems.Add(problem);
        else values[definition.Key] = definition.IsSecret ? change.Value : value; // a secret as typed: a space at either end may be part of it
    }

    /// <summary>
    /// The start's checks pass, but a running app knows more: Standalone needs an administrator to sign in with afterwards (one there, or one
    /// the first start can create), and an OIDC mode or authority the next start would take up must answer with itself as the issuer.
    /// </summary>
    private async Task CheckRulesOnlyARunningAppKnowsAsync(IConfiguration candidate, CancellationToken ct)
    {
        var mode = candidate["Auth:Mode"];
        await RequireAdministratorAsync(candidate, ct);

        // Whatever this save changed: the next start may take up a mode or an authority that does not run now (a file left out at start holds
        // one). The sign-in waits for a restart, so what runs now is what the process started with, not the configuration a save reloaded.
        var running = configuration.Started;
        if (Is(mode, AuthMode.Oidc) && (!Is(running["Auth:Mode"], AuthMode.Oidc) || !SameText(candidate["Auth:Oidc:Authority"], running["Auth:Oidc:Authority"])))
        {
            var authority = candidate["Auth:Oidc:Authority"] ?? "";
            var answer = await discovery.ProbeAsync(authority, ct);
            if (!answer.Reachable)
                throw new DomainException("settings.oidcUnreachable", $"{authority} does not answer with an OpenID Connect discovery document ({answer.Reason}).", new { Authority = authority, Reason = answer.Reason });
            if (!SameIssuer(authority, answer.Issuer!))
                throw new DomainException("settings.oidcIssuerMismatch", $"{authority} names another issuer, {answer.Issuer}: enter the authority as the provider names it.", new { Authority = authority, Issuer = answer.Issuer });
        }
    }

    private static bool Is(string? mode, AuthMode expected) => Enum.TryParse<AuthMode>(mode, ignoreCase: true, out var parsed) && parsed == expected;

    private static bool SameText(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);

    /// <summary>Standalone needs an administrator to sign in with afterwards: one there, or one the first start can create from the credentials.</summary>
    private async Task RequireAdministratorAsync(IConfiguration next, CancellationToken ct)
    {
        if (Is(next["Auth:Mode"], AuthMode.Standalone) && !await users.AnyLocalAdminAsync(ct) && await BootstrapProblemAsync(next, ct) is { } reason)
            throw new DomainException("settings.standaloneNeedsAdministrator",
                $"Standalone mode needs an administrator: none exists yet, and the next start creates the first one from Auth:Standalone:AdminEmail (a valid address no local account has) and Auth:Standalone:AdminPassword ({PasswordPolicy.MinLength} to {PasswordPolicy.MaxLength} characters) in the environment.",
                new { Reason = reason });
    }

    /// <summary>
    /// Why the first start in Standalone could not create the administrator from the candidate's credentials, as AuthService.BootstrapAdminAsync
    /// will try to (null when it could): both must be given, the password must pass the policy, the address must be one, and no local account
    /// may hold it yet (a disabled administrator or a user does: the new account could not be added beside it).
    /// </summary>
    private async Task<string?> BootstrapProblemAsync(IConfiguration candidate, CancellationToken ct)
    {
        var email = candidate["Auth:Standalone:AdminEmail"];
        var password = candidate["Auth:Standalone:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return "missing";
        try
        {
            PasswordPolicy.Validate(password);
        }
        catch (DomainException)
        {
            return "password";
        }
        string address;
        try
        {
            address = User.NormalizeEmail(email);
        }
        catch (DomainException)
        {
            return "email";
        }
        return await users.FindLocalByEmailAsync(address, ct) is null ? null : "taken";
    }

    /// <summary>The same place, written either way: a trailing slash or the case of the scheme and host make no difference.</summary>
    public static bool SameIssuer(string authority, string issuer) =>
        Uri.TryCreate(authority.TrimEnd('/'), UriKind.Absolute, out var a) && Uri.TryCreate(issuer.TrimEnd('/'), UriKind.Absolute, out var b)
            ? a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase) && a.Authority.Equals(b.Authority, StringComparison.OrdinalIgnoreCase) && a.AbsolutePath == b.AbsolutePath
            : authority.TrimEnd('/') == issuer.TrimEnd('/');

    private static DomainException Invalid(List<string> problems) =>
        new("settings.invalid", $"The settings cannot be used as they are: {string.Join(" ", problems)}", new { Problems = problems });

    /// <summary>
    /// The file as it is, or null when it cannot be read (not JSON, or one the app's user may not open): the page shows it as empty, and a
    /// save replaces it only when the start left it out (see <see cref="UpdateAsync"/>).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string?>?> ReadFileAsync(CancellationToken ct)
    {
        try
        {
            return await store.ReadAsync(ct);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The settings file {Path} cannot be read; the page treats it as empty", store.Path);
            return null;
        }
    }

    private async Task<SystemSettingsView> ViewAsync(CancellationToken ct)
    {
        var candidate = configuration.With(await ReadFileAsync(ct) ?? new Dictionary<string, string?>());
        try
        {
            var groups = new List<SettingsGroupView>();
            foreach (var group in SettingsCatalog.Groups)
            {
                var settings = new List<SettingView>();
                foreach (var definition in SettingsCatalog.Of(group))
                {
                    var source = configuration.SourceOf(definition.Key);
                    var next = ValueOf(candidate, definition);
                    // What it runs with: a setting that applies at once runs with the reloaded configuration, one that waits for a restart
                    // still with the value it started with. So a restart is pending only where a restart is really needed.
                    var running = ValueOf(definition.AppliesAtOnce ? configuration.Running : configuration.Started, definition);
                    settings.Add(new SettingView(definition, definition.IsSecret ? null : next, !string.IsNullOrEmpty(next), source,
                        SettingView.IsEditable(source) && !string.Equals(next, running, StringComparison.Ordinal)));
                }
                groups.Add(new SettingsGroupView(group, settings));
            }
            return new SystemSettingsView(configuration.File with { Exists = store.Exists }, groups);
        }
        finally
        {
            (candidate as IDisposable)?.Dispose();
        }
    }

    /// <summary>A setting's value in a configuration; a list as its items, one per line.</summary>
    public static string? ValueOf(IConfiguration configuration, SettingDefinition definition)
    {
        if (!definition.IsList) return configuration[definition.Key];
        var items = configuration.GetSection(definition.Key).GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return items.Count == 0 ? null : string.Join("\n", items);
    }
}
