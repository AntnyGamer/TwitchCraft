using System.Reflection;
using System.Runtime.InteropServices;
using TwitchCraft_V1;

namespace TwitchCraft.Tests.Runtime;

public sealed class NativeInteropSafetyTests
{
    [Fact]
    public void RestartManagerImports_AreRestrictedToSystem32()
    {
        MethodInfo[] imports = typeof(MainHandler)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method =>
                string.Equals(
                    method.GetCustomAttribute<LibraryImportAttribute>()?.LibraryName,
                    "rstrtmgr.dll",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    method.GetCustomAttribute<DllImportAttribute>()?.Value,
                    "rstrtmgr.dll",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(imports);
        Assert.All(imports, method =>
            Assert.Equal(
                DllImportSearchPath.System32,
                method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths));
    }
}
