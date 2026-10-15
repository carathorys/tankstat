using System.Globalization;
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

    /// <summary>
    /// The minimum a configuration (a candidate's, a restored file's) sets, or the floor where it sets none or one that is no number (the
    /// validator says that; this only answers what a password must have meanwhile).
    /// </summary>
    public static int MinLengthIn(Func<string, string?> value) =>
        int.TryParse(value(MinLengthKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) ? Math.Max(min, PasswordPolicy.MinLength) : PasswordPolicy.MinLength;
}

public sealed class PasswordOptionsValidator : IValidateOptions<PasswordOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordOptions options) =>
        options.PasswordMinLength is < PasswordPolicy.MinLength or > PasswordPolicy.MaxLength
            ? ValidateOptionsResult.Fail(
                $"Auth:Standalone:PasswordMinLength must be from {PasswordPolicy.MinLength} to {PasswordPolicy.MaxLength}.")
            : ValidateOptionsResult.Success;
}

/// <summary>The password requirements as they are now (a save on the settings page applies at once): every new password is judged by them.</summary>
public sealed class PasswordRequirements(IOptionsMonitor<PasswordOptions> options)
{
    public int MinLength => options.CurrentValue.PasswordMinLength;

    public static int MaxLength => PasswordPolicy.MaxLength;

    /// <summary><c>password.tooShort</c> (with the minimum) or <c>password.tooLong</c>.</summary>
    public void Validate(string? password) => PasswordPolicy.Validate(password, MinLength);

    public bool Meets(string? password) => PasswordPolicy.Meets(password, MinLength);
}
