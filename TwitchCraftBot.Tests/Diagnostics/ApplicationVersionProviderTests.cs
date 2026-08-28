using System.Reflection;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Diagnostics;

public sealed class ApplicationVersionProviderTests
{
    [Fact]
    public void Resolve_UsesTheApplicationFileVersion()
    {
        Assembly applicationAssembly = typeof(ApplicationVersionProvider).Assembly;

        string version = ApplicationVersionProvider.Resolve(applicationAssembly);

        Assert.Equal("1.8.0.0", version);
        Assert.Equal(version, ApplicationVersionProvider.Resolve());
        Assert.NotEqual(applicationAssembly.GetName().Version?.ToString(), version);
    }

    [Fact]
    public void Resolve_ReturnsUnknownWhenAssemblyMetadataIsUnavailable()
    {
        Assert.Equal(ApplicationVersionProvider.UnknownVersion, ApplicationVersionProvider.Resolve(null));
    }
}
