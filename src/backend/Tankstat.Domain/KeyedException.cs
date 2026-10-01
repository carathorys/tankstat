using System.Reflection;

namespace Tankstat.Domain;

/// <summary>
/// An error with a stable <see cref="Key"/> and named <see cref="Args"/>, so clients can translate it
/// (the English <see cref="Exception.Message"/> is only a fallback for logs and untranslated clients).
/// </summary>
public abstract class KeyedException : Exception
{
    protected KeyedException(string key, string message, object? args = null) : base(message)
    {
        Key = key;
        Args = args is null
            ? new Dictionary<string, object?>()
            : args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.GetValue(args)); // camelCase: matches {{placeholders}}
    }

    /// <summary>Dotted identifier such as <c>vehicle.nameRequired</c>.</summary>
    public string Key { get; }

    /// <summary>Values to interpolate into the translated message, keyed in camelCase (e.g. <c>min</c>).</summary>
    public IReadOnlyDictionary<string, object?> Args { get; }
}
