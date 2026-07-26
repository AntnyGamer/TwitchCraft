using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class JavaProcessLifecycleTests
{
    [Fact]
    public async Task JavaProcess_StartsWithVersionedArgumentsAndAcceptsCommands()
    {
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "normal");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        try
        {
            await runtime.StartJavaServerAsync(config, CancellationToken.None);
            await runtime.EnsureServerProcessStartedAsync(CancellationToken.None);
            Assert.True(await runtime.SendServerCommandAsync("say integration-test", CancellationToken.None));
            Assert.True(await runtime.SendServerCommandAsync("stop", CancellationToken.None));
            await WaitForLineCountAsync(config.Server.JarPath + ".stdin", 2);
            await runtime.SafeStopProcessAsync(waitBriefly: true);

            string[] arguments = await File.ReadAllLinesAsync(
                config.Server.JarPath + ".args",
                TestContext.Current.CancellationToken);
            string[] commands = await File.ReadAllLinesAsync(
                config.Server.JarPath + ".stdin",
                TestContext.Current.CancellationToken);
            Assert.Equal(
                [
                    "-Xmx4G",
                    "-Xms2G",
                    "--enable-native-access=ALL-UNNAMED",
                    "-jar",
                    config.Server.JarPath,
                    "nogui"
                ],
                arguments);
            Assert.Equal(["say integration-test", "stop"], commands);
        }
        finally
        {
            await runtime.SafeStopProcessAsync(waitBriefly: false);
        }
    }

    [Fact]
    public async Task EnsureServerProcessStartedAsync_DetectsImmediateJavaExit()
    {
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "exit-immediately");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        try
        {
            await runtime.StartJavaServerAsync(config, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runtime.EnsureServerProcessStartedAsync(CancellationToken.None));
        }
        finally
        {
            await runtime.SafeStopProcessAsync(waitBriefly: false);
        }
    }

    [Fact]
    public async Task SafeStopProcessAsync_ForceStopsJavaThatIgnoresStop()
    {
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "ignore-stop");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        await runtime.StartJavaServerAsync(config, CancellationToken.None);
        await runtime.EnsureServerProcessStartedAsync(CancellationToken.None);
        Assert.True(await runtime.SendServerCommandAsync("stop", CancellationToken.None));
        await WaitForLineCountAsync(config.Server.JarPath + ".stdin", 1);

        await runtime.SafeStopProcessAsync(waitBriefly: false);

        Assert.False(await runtime.SendServerCommandAsync("say after-stop", CancellationToken.None));
    }

    [Fact]
    public async Task StartJavaServerAsync_RejectsMissingServerJar()
    {
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "normal");
        File.Delete(config.Server.JarPath);
        BotMainHandler runtime = CreateRuntime(directory.Path);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            runtime.StartJavaServerAsync(config, CancellationToken.None));
    }

    private static BotMainHandler CreateRuntime(string directory)
        => new(
            new AppShellViewModel(),
            System.IO.Path.Combine(directory, "viewer_tokens.db"),
            initializeApplicationState: false);

    private static BotConfig CreateConfig(string directory, string mode)
    {
        string jarPath = System.IO.Path.Combine(directory, "server.jar");
        File.WriteAllText(jarPath, mode);

        return new BotConfig
        {
            Server = new ServerConfig
            {
                Java = new JavaConfig { ExecutablePath = GetFakeJavaExecutable() },
                ServerDirectory = directory,
                JarPath = jarPath,
                MinecraftVersion = "26.1.0",
                MemoryMinGB = 2,
                MemoryMaxGB = 4
            }
        };
    }

    private static string GetFakeJavaExecutable()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(System.IO.Path.Combine(root.FullName, "TwitchCraft.slnx")))
            root = root.Parent;

        Assert.NotNull(root);
        string configuration = AppContext.BaseDirectory.Contains(
            System.IO.Path.DirectorySeparatorChar + "Release" + System.IO.Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string executable = System.IO.Path.Combine(
            root.FullName,
            "TwitchCraftBot.TestProcess",
            "bin",
            configuration,
            "net10.0",
            "TwitchCraftBot.TestProcess.exe");
        Assert.True(File.Exists(executable), "Fake Java process was not built: " + executable);
        return executable;
    }

    private static async Task WaitForLineCountAsync(string path, int expectedLineCount)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!File.Exists(path) || ReadAllLinesShared(path).Count < expectedLineCount)
            await Task.Delay(20, timeout.Token);
    }

    private static List<string> ReadAllLinesShared(string path)
    {
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
