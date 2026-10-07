using System;
using System.IO;
using System.Reflection;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Frames;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class TemporaryAuditVerificationTests
{
    [Fact]
    public void PendingBackupCleanup_DeletesOnlyStaleAppPendingDirectories()
    {
        using TemporaryDirectory root = new();
        string stale = Path.Combine(root.Path, ".pending-" + Guid.NewGuid().ToString("N"));
        string fresh = Path.Combine(root.Path, ".pending-" + Guid.NewGuid().ToString("N"));
        string unrelated = Path.Combine(root.Path, ".pending-" + new string('a', 31) + "x-extra");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(unrelated);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        DataMaintenance.PruneBackups(root.Path, retentionCount: 3);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void MojangUriTrust_AcceptsOnlyHttpsMojangHosts()
    {
        MethodInfo method = typeof(Setup).GetMethod("IsTrustedMinecraftUri", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Mojang URI trust helper was not found.");

        bool Trusted(string url) => (bool)(method.Invoke(null, [new Uri(url)]) ?? false);

        Assert.True(Trusted("https://mojang.com/file"));
        Assert.True(Trusted("https://piston-data.mojang.com/file"));
        Assert.True(Trusted("https://launchermeta.mojang.com/file"));
        Assert.False(Trusted("http://piston-data.mojang.com/file"));
        Assert.False(Trusted("https://mojang.com.example.test/file"));
        Assert.False(Trusted("https://evilmojang.com/file"));
        Assert.False(Trusted("https://example.com/file"));
    }
}
