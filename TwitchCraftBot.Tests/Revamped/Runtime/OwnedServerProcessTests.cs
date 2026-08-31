using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class OwnedServerProcessTests
{
    [Fact]
    public async Task JavaProcess_StartsWithVersionedArgumentsAndAcceptsCommands()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);
            await runtime.StartServerIfNeededAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("say integration-test", cancellationToken));
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 2, cancellationToken);
            await runtime.StopProcessSafeAsync(waitBriefly: true);

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
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task StartServerIfNeededAsync_DetectsImmediateJavaExit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path, "exit-immediately");
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);

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
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path, "ignore-stop");
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.StartServerAsync(config, cancellationToken);
            await runtime.StartServerIfNeededAsync(cancellationToken);
            Assert.True(await runtime.SendServerCommandAsync("stop", cancellationToken));
            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 1, cancellationToken);
            int processId = int.Parse(
                await File.ReadAllTextAsync(config.Server.JarPath + ".pid", cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);

            await runtime.StopProcessSafeAsync(waitBriefly: false);

            Assert.False(await runtime.SendServerCommandAsync("say after-stop", cancellationToken));
            await FakeJavaServer.WaitForProcessExitAsync(processId, cancellationToken);
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
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        File.Delete(config.Server.JarPath);
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

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
