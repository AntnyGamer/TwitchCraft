using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class OwnedServerProcessTests
{
    [Fact]
    public async Task JavaProcess_StartsWithVersionedArgumentsAndAcceptsCommands()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);
            await runtime.StartServerIfNeededAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("say integration-test", cancellationToken));
            Task stopTask = runtime.StopProcessSafeAsync(waitBriefly: true);
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await stopTask;

            string[] arguments = await File.ReadAllLinesAsync(config.Server.JarPath + ".args", cancellationToken);
            string[] commands = await File.ReadAllLinesAsync(config.Server.JarPath + ".stdin", cancellationToken);
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
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task StartServerIfNeededAsync_DetectsImmediateJavaExit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "exit-immediately");
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);
            string processIDPath = config.Server.JarPath + ".pid";
            int processID = 0;
            await FakeJavaServer.WaitUntilAsync(
                () =>
                {
                    try
                    {
                        return int.TryParse(File.ReadAllText(processIDPath), out processID);
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                },
                "Fake Java process did not write its process ID within 10 seconds.",
                cancellationToken);
            await FakeJavaServer.WaitForProcessExitAsync(processID, cancellationToken);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runtime.StartServerIfNeededAsync(cancellationToken));
            Assert.Contains("exited during startup", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task StopProcessSafeAsync_ForceStopsJavaThatIgnoresStop()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ignore-stop");
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);
            await runtime.StartServerIfNeededAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 1, cancellationToken);
            int processID = int.Parse(
                await File.ReadAllTextAsync(config.Server.JarPath + ".pid", cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);

            await runtime.StopProcessSafeAsync(waitBriefly: false);

            Assert.False(await runtime.SendServerCommandAsync("say after-stop", cancellationToken));
            await FakeJavaServer.WaitForProcessExitAsync(processID, cancellationToken);
        }
        finally
        {
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task StartServerAsync_RejectsMissingServerJar()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        File.Delete(config.Server.JarPath);
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                runtime.StartServerAsync(config, cancellationToken));
            Assert.Equal(config.Server.JarPath, exception.FileName);
        }
        finally
        {
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }
}
