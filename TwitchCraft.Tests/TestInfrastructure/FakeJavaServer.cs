using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.TestInfrastructure;

internal static class FakeJavaServer
{
    internal static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(10);

    internal static TwitchCraftConfig CreateConfig(string directory, string mode = "normal")
    {
        string jarPath = Path.Combine(directory, "server.jar");
        File.WriteAllText(jarPath, mode);

        return new TwitchCraftConfig
        {
            Twitch = new TwitchConfig
            {
                StreamerName = "streamer",
                BotName = "twitchcraft"
            },
            Server = new ServerConfig
            {
                Java = new JavaConfig { ExecutablePath = GetExecutable() },
                RCON = new RCONConfig { Port = 25575, Password = "test-password" },
                ServerDirectory = directory,
                JarPath = jarPath,
                MinecraftVersion = "26.1.0",
                MemoryMinGB = 2,
                MemoryMaxGB = 4
            },
            Settings = new StartingProfile
            {
                StatisticsEnabled = false,
                MinigamesEnabled = false,
                PassiveTokenEarningEnabled = false,
                AutomaticFollowRewardsEnabled = false,
                AutomaticBitRewardsEnabled = false,
                AutomaticBackupsEnabled = false,
                NonCommandChatRelayEnabled = false,
                GlobalGameCommandCooldownEnabled = false
            }
        };
    }

    internal static MainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            Path.Combine(directory, "viewer_tokens.db"));

    internal static void ConfigureResponsiveServer(
        string jarPath,
        IReadOnlyList<string> players,
        IReadOnlyList<string>? spectators = null,
        double maxHealth = 20,
        string? selectedItem = null,
        string? attributes = null)
    {
        File.WriteAllText(jarPath + ".players", string.Join(",", players));
        File.WriteAllText(jarPath + ".spectators", string.Join(",", spectators ?? []));
        File.WriteAllText(jarPath + ".health", maxHealth.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(jarPath + ".item", selectedItem ?? "{id:'minecraft:air',count:1}");
        File.WriteAllText(jarPath + ".attributes", attributes ?? "[]");
        File.WriteAllText(jarPath + ".probe-delay", "0");
    }

    internal static string GetExecutable()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TwitchCraft.slnx")))
            root = root.Parent;

        Assert.NotNull(root);
        string configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string executable = Path.Combine(
            root.FullName,
            "TwitchCraft.Tests",
            "TestInfrastructure",
            "TestProcess",
            "bin",
            configuration,
            "net10.0",
            "TwitchCraft.TestProcess.exe");
        Assert.True(File.Exists(executable), "Fake Java process was not built: " + executable);
        return executable;
    }

    internal static Task WaitForReadyAsync(MainHandler runtime, CancellationToken cancellationToken)
        => WaitUntilAsync(
            () => runtime.MinecraftServerReady,
            "TwitchCraft did not observe the fake Minecraft server ready line within 10 seconds.",
            cancellationToken);

    internal static Task WaitForLineCountAsync(
        string path,
        int expectedLineCount,
        CancellationToken cancellationToken)
        => WaitUntilAsync(
            () => File.Exists(path) && ReadAllLinesShared(path).Count >= expectedLineCount,
            $"Expected at least {expectedLineCount} line(s) in '{path}' within 10 seconds.",
            cancellationToken);

    internal static Task WaitForProcessExitAsync(int processID, CancellationToken cancellationToken)
        => WaitUntilAsync(
            () =>
            {
                try
                {
                    using Process process = Process.GetProcessById(processID);
                    return process.HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            },
            $"Process {processID} did not exit within 10 seconds.",
            cancellationToken);

    internal static async Task WaitUntilAsync(
        Func<bool> condition,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PollingTimeout);
        try
        {
            while (!condition())
                await Task.Delay(20, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    internal static async Task StopRuntimeAndProcessAsync(MainHandler runtime, string jarPath)
    {
        await runtime.StopProcessSafeAsync(waitBriefly: false);
        runtime.Tokens.Close();

        string processIDPath = jarPath + ".pid";
        if (!File.Exists(processIDPath))
            return;

        int processID = int.Parse(
            await File.ReadAllTextAsync(processIDPath),
            System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using Process process = Process.GetProcessById(processID);
            if (process.HasExited)
                return;

            string? executablePath = process.MainModule?.FileName;
            if (!string.Equals(executablePath, GetExecutable(), StringComparison.OrdinalIgnoreCase))
                return;

            process.Kill(entireProcessTree: true);

            using CancellationTokenSource timeout = new(PollingTimeout);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // The process has already exited and left the process table.
        }
    }

    internal static List<string> ReadAllLinesShared(string path)
    {
        if (!File.Exists(path))
            return [];

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        List<string> lines = [];
        while (reader.ReadLine() is string line)
            lines.Add(line);
        return lines;
    }
}
