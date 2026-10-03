using System.Reflection;

namespace Tankstat.Reader;

/// <summary>The version the image was built as (<c>-p:Version</c>), without the commit suffix the SDK appends.</summary>
public static class ReaderVersion
{
    public static string Value { get; } =
        (typeof(ReaderVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
