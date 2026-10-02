using System.IO;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class BackupRetentionPolicyTests
{
    [Fact]
    public void PruneBackups_PreservesUnrelatedTimestampLikeDirectories()
    {
        using TemporaryDirectory root = new();
        CreateCompleteBackup(root.Path, "20260830-231500");
        string[] unrelatedNames = ["2026-08-30-231500", "20260830-231500-nothex", "20260230-120000"];
        foreach (string name in unrelatedNames)
        {
            string unrelated = Path.Combine(root.Path, name);
            Directory.CreateDirectory(unrelated);
            File.WriteAllText(Path.Combine(unrelated, "keep.txt"), name);
        }

        DataMaintenance.PruneBackups(root.Path, retentionCount: 1);

        foreach (string name in unrelatedNames)
            Assert.Equal(name, File.ReadAllText(Path.Combine(root.Path, name, "keep.txt")));
    }

    [Fact]
    public void PruneBackups_DefaultRetentionKeepsThreeNewestCompleteBackups()
    {
        using TemporaryDirectory root = new();
        string[] names =
        [
            "20260826-120000",
            "20260827-120000",
            "20260828-120000",
            "20260829-120000",
            "20260830-120000-a1b2c3"
        ];
        foreach (string name in names)
            CreateCompleteBackup(root.Path, name);

        DataMaintenance.PruneBackups(root.Path, retentionCount: 3);

        Assert.False(Directory.Exists(Path.Combine(root.Path, names[0])));
        Assert.False(Directory.Exists(Path.Combine(root.Path, names[1])));
        Assert.All(names[2..], name => Assert.True(Directory.Exists(Path.Combine(root.Path, name))));
    }

    [Fact]
    public void PruneBackups_OneBackupRemovesOldAndIncompleteButPreservesUnrelatedFolders()
    {
        using TemporaryDirectory root = new();
        CreateCompleteBackup(root.Path, "20260828-120000");
        CreateCompleteBackup(root.Path, "20260830-120000");
        Directory.CreateDirectory(Path.Combine(root.Path, "20260829-120000"));
        Directory.CreateDirectory(Path.Combine(root.Path, "notes"));
        string configOnly = Path.Combine(root.Path, "20260831-120000");
        Directory.CreateDirectory(configOnly);
        File.WriteAllText(Path.Combine(configOnly, "config.json"), "{}");
        string tokensOnly = Path.Combine(root.Path, "20260901-120000");
        Directory.CreateDirectory(tokensOnly);
        File.WriteAllBytes(Path.Combine(tokensOnly, "viewer_tokens.db"), [2]);

        DataMaintenance.PruneBackups(root.Path, retentionCount: 1);

        Assert.False(Directory.Exists(Path.Combine(root.Path, "20260828-120000")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "20260829-120000")));
        Assert.False(Directory.Exists(configOnly));
        Assert.False(Directory.Exists(tokensOnly));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "20260830-120000")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root.Path, "20260830-120000", "config.json")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(root.Path, "20260830-120000", "viewer_tokens.db")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "notes")));
    }

    private static void CreateCompleteBackup(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "config.json"), "{}");
        File.WriteAllBytes(Path.Combine(path, "viewer_tokens.db"), [1]);
    }
}
