using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>
/// The password requirements administrators set, for Standalone sign-in (#120). A section of its own beside <c>AuthOptions</c>, which is read
/// once at start: these apply at once after a save (bound with a change token, read through <see cref="PasswordRequirements"/>).
/// </summary>
public sealed class PasswordOptions
{
    public const string SectionName = "Auth:Standalone";

    /// <summary>The least a password must have, from <see cref="PasswordPolicy.MinLength"/> to <see cref="PasswordPolicy.MaxLength"/>.</summary>
    public int PasswordMinLength { get; set; } = PasswordPolicy.MinLength;

    public const string MinLengthKey = SectionName + ":" + nameof(PasswordMinLength);
}

public sealed class PasswordOptionsValidator : IValidateOptions<PasswordOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordOptions options) =>
        options.PasswordMinLength is < PasswordPolicy.MinLength or > PasswordPolicy.MaxLength
            ? ValidateOptionsResult.Fail(
                $"Auth:Standalone:PasswordMinLength must be from {PasswordPolicy.MinLength} to {PasswordPolicy.MaxLength}.")
            : ValidateOptionsResult.Success;
}

/// <summary>
/// The password requirements as they are now (a save on the settings page applies at once): every new password is judged by them. A minimum
/// that stops being usable while the app runs (an <c>appsettings.json</c> edited by hand reloads on its own; the settings page never saves
/// one) is said once by a Warning, and the last usable one stays in force until it is fixed, so sign-in and the password forms go on working,
/// as the backup schedule does with its settings.
/// </summary>
public sealed class PasswordRequirements
{
    private readonly IOptionsMonitor<PasswordOptions> _options;
    private readonly ILogger<PasswordRequirements> _logger;
    private int _kept = PasswordPolicy.MinLength;
    private int _unusable;

    public PasswordRequirements(IOptionsMonitor<PasswordOptions> options, ILogger<PasswordRequirements> logger)
    {
        (_options, _logger) = (options, logger);
        Read(); // made at every start right after the options were validated: the minimum the app started with is the first kept
    }

    public int MinLength => Read() ?? Volatile.Read(ref _kept);

    public static int MaxLength => PasswordPolicy.MaxLength;

    /// <summary><c>password.tooShort</c> (with the minimum) or <c>password.tooLong</c>.</summary>
    public void Validate(string? password) => PasswordPolicy.Validate(password, MinLength);

    public bool Meets(string? password) => PasswordPolicy.Meets(password, MinLength);

    /// <summary>The minimum as configured now, kept as the last usable one; null while it cannot be used.</summary>
    private int? Read()
    {
        int min;
        try
        {
            min = _options.CurrentValue.PasswordMinLength;
        }
        catch (Exception e) when (e is OptionsValidationException or InvalidOperationException)
        {
            // The binder's own words quote the value, so only the validator's are logged.
            if (Interlocked.Exchange(ref _unusable, 1) == 0)
                _logger.LogWarning("The minimum password length cannot be used, so new passwords are judged by the last usable one, {MinLength}, until it is fixed: {Problems}",
                    Volatile.Read(ref _kept), e is OptionsValidationException invalid ? string.Join(" ", invalid.Failures) : "a value cannot be read as its type.");
            return null;
        }
        Volatile.Write(ref _kept, min);
        if (Volatile.Read(ref _unusable) == 1 && Interlocked.Exchange(ref _unusable, 0) == 1)
            _logger.LogInformation("The minimum password length can be used again: {MinLength}", min);
        return min;
    }
}
