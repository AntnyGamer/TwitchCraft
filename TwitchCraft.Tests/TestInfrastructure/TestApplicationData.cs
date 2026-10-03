using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TwitchCraft.Tests.TestInfrastructure;

internal static class TestApplicationData
{
    internal static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "TwitchCraftTests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Path);
        AppContext.SetData("TwitchCraft.DataDirectory", Path);
    }
}
