using System.Diagnostics;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.TestInfrastructure;

internal static class FakeJavaServer
{
    internal static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(10);

    internal static BotConfig CreateConfig(string directory, string mode = "normal")
    {
        string jarPath = Path.Combine(directory, "server.jar");
        File.WriteAllText(jarPath, mode);

        return new BotConfig
        {
            Twitch = new TwitchConfig
            {
                StreamerName = "streamer",
                BotName = "twitchcraftbot"
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

    internal static BotMainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            Path.Combine(directory, "viewer_tokens.db"));

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
            "TwitchCraftBot.Tests",
            "TestInfrastructure",
            "TestProcess",
            "bin",
            configuration,
            "net10.0",
            "TwitchCraftBot.TestProcess.exe");
        Assert.True(File.Exists(executable), "Fake Java process was not built: " + executable);
        return executable;
    }

    internal static async Task WaitForReadyAsync(BotMainHandler runtime, CancellationToken cancellationToken)
        => await WaitUntilAsync(
            () => runtime.MinecraftServerReady,
            "TwitchCraft did not observe the fake Minecraft server ready line within 10 seconds.",
            cancellationToken);

    internal static async Task WaitForLineCountAsync(
        string path,
        int expectedLineCount,
        CancellationToken cancellationToken)
        => await WaitUntilAsync(
            () => File.Exists(path) && ReadAllLinesShared(path).Count >= expectedLineCount,
            $"Expected at least {expectedLineCount} line(s) in '{path}' within 10 seconds.",
            cancellationToken);

    internal static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
        => await WaitUntilAsync(
            () =>
            {
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    return process.HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            },
            $"Process {processId} did not exit within 10 seconds.",
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

    internal static async Task StopRuntimeAndProcessAsync(BotMainHandler runtime, string jarPath)
    {
        await runtime.StopProcessSafeAsync(waitBriefly: false);
        runtime.Tokens.Close();

        string processIdPath = jarPath + ".pid";
        if (!File.Exists(processIdPath))
            return;

        int processId = int.Parse(
            await File.ReadAllTextAsync(processIdPath),
            System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using Process process = Process.GetProcessById(processId);
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
