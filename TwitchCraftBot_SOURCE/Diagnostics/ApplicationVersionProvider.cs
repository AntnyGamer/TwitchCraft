using System.Reflection;

namespace TwitchCraftBot_V1;

internal static class ApplicationVersionProvider
{
    internal const string UnknownVersion = "unknown";

    internal static string Resolve()
        => Resolve(typeof(ApplicationVersionProvider).Assembly);

    internal static string Resolve(Assembly? assembly)
    {
        if (assembly == null)
            return UnknownVersion;

        try
        {
            string? fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            return string.IsNullOrWhiteSpace(fileVersion) ? UnknownVersion : fileVersion;
        }
        catch
        {
            return UnknownVersion;
        }
    }
}
