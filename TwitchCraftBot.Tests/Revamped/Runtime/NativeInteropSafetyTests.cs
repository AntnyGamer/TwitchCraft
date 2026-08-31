using System.Reflection;
using System.Runtime.InteropServices;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class NativeInteropSafetyTests
{
    [Fact]
    public void RestartManagerImports_AreRestrictedToSystem32()
    {
        MethodInfo[] imports = typeof(BotMainHandler)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("RM", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, imports.Length);
        Assert.All(imports, method =>
            Assert.Equal(
                DllImportSearchPath.System32,
                method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths));
    }
}
