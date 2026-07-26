using System.Diagnostics;
using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class JavaProcessLifecycleTests
{
    [Fact]
    public async Task JavaProcess_StartsWithVersionedArgumentsAndAcceptsCommands()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "normal");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        try
        {
            await runtime.StartJavaServerAsync(config, cancellationToken);
            await runtime.EnsureServerProcessStartedAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("say integration-test", cancellationToken));
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await WaitForLineCountAsync(config.Server.JarPath + ".stdin", 2, cancellationToken);
            await runtime.SafeStopProcessAsync(waitBriefly: true);

            string[] arguments = await File.ReadAllLinesAsync(
                config.Server.JarPath + ".args",
                cancellationToken);
            string[] commands = await File.ReadAllLinesAsync(
                config.Server.JarPath + ".stdin",
                cancellationToken);
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
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "exit-immediately");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        try
        {
            await runtime.StartJavaServerAsync(config, cancellationToken);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runtime.EnsureServerProcessStartedAsync(cancellationToken));
            Assert.Contains("exited during startup", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await runtime.SafeStopProcessAsync(waitBriefly: false);
        }
    }

    [Fact]
    public async Task SafeStopProcessAsync_ForceStopsJavaThatIgnoresStop()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "ignore-stop");
        BotMainHandler runtime = CreateRuntime(directory.Path);

        try
        {
            await runtime.StartJavaServerAsync(config, cancellationToken);
            await runtime.EnsureServerProcessStartedAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await WaitForLineCountAsync(config.Server.JarPath + ".stdin", 1, cancellationToken);
            int processId = int.Parse(
                await File.ReadAllTextAsync(config.Server.JarPath + ".pid", cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);

            await runtime.SafeStopProcessAsync(waitBriefly: false);

            Assert.False(await runtime.SendServerCommandAsync("say after-stop", cancellationToken));
            await WaitForProcessExitAsync(processId, cancellationToken);
        }
        finally
        {
            await runtime.SafeStopProcessAsync(waitBriefly: false);
        }
    }

    [Fact]
    public async Task StartJavaServerAsync_RejectsMissingServerJar()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = CreateConfig(directory.Path, "normal");
        File.Delete(config.Server.JarPath);
        BotMainHandler runtime = CreateRuntime(directory.Path);

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            runtime.StartJavaServerAsync(config, cancellationToken));
        Assert.Equal(config.Server.JarPath, exception.FileName);
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

    private static async Task WaitForLineCountAsync(
        string path,
        int expectedLineCount,
        CancellationToken cancellationToken)
        => await WaitUntilAsync(
            () => File.Exists(path) && ReadAllLinesShared(path).Count >= expectedLineCount,
            $"Expected at least {expectedLineCount} line(s) in '{path}' within three seconds.",
            cancellationToken);

    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
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
            $"Process {processId} did not exit within three seconds.",
            cancellationToken);

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
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
