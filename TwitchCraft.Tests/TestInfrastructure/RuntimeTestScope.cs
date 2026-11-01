using System;
using System.Threading.Tasks;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;

namespace TwitchCraft.Tests.TestInfrastructure;

internal sealed class RuntimeTestScope : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    internal MainHandler Runtime
    { get; }

    internal RuntimeTestScope()
    {
        try
        {
            Runtime = FakeJavaServer.CreateRuntime(_directory.Path);
        }
        catch
        {
            _directory.Dispose();
            throw;
        }
    }

    internal static async Task<RuntimeTestScope> CreateAsync(Action<StartingProfile> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        RuntimeTestScope scope = new();
        try
        {
            TwitchCraftConfig config = new();
            configure(config.Settings);
            await scope.Runtime.ApplySettingsAsync(config);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            Runtime.Tokens.Close();
        }
        finally
        {
            _directory.Dispose();
        }
    }
}
