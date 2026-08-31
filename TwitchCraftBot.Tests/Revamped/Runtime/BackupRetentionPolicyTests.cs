using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class BackupRetentionPolicyTests
{
    [Fact]
    public void PruneBackups_CollisionSuffixParticipatesInTimestampOrdering()
    {
        using TemporaryDirectory root = new();
        CreateCompleteBackup(root.Path, "20260830-231500-a1b2c3");
        CreateCompleteBackup(root.Path, "20260829-231500");

        BotMainHandler.PruneBackups(root.Path, retentionCount: 1);

        Assert.True(Directory.Exists(Path.Combine(root.Path, "20260830-231500-a1b2c3")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "20260829-231500")));
    }

    [Fact]
    public void PruneBackups_PreservesUnrelatedTimestampLikeDirectories()
    {
        using TemporaryDirectory root = new();
        CreateCompleteBackup(root.Path, "20260830-231500");
        string unrelated = Path.Combine(root.Path, "2026-08-30-231500");
        Directory.CreateDirectory(unrelated);

        BotMainHandler.PruneBackups(root.Path, retentionCount: 1);

        Assert.True(Directory.Exists(unrelated));
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
            "20260830-120000"
        ];
        foreach (string name in names)
            CreateCompleteBackup(root.Path, name);

        BotMainHandler.PruneBackups(root.Path, retentionCount: 3);

        Assert.False(Directory.Exists(Path.Combine(root.Path, names[0])));
        Assert.False(Directory.Exists(Path.Combine(root.Path, names[1])));
        Assert.All(names[2..], name => Assert.True(Directory.Exists(Path.Combine(root.Path, name))));
    }

    [Fact]
    public void PruneBackups_OneBackupOptionRemovesOlderAndIncompleteButPreservesUnrelatedFolders()
    {
        using TemporaryDirectory root = new();
        CreateCompleteBackup(root.Path, "20260828-120000");
        CreateCompleteBackup(root.Path, "20260830-120000");
        Directory.CreateDirectory(Path.Combine(root.Path, "20260829-120000"));
        Directory.CreateDirectory(Path.Combine(root.Path, "notes"));

        BotMainHandler.PruneBackups(root.Path, retentionCount: 1);

        Assert.False(Directory.Exists(Path.Combine(root.Path, "20260828-120000")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "20260829-120000")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "20260830-120000")));
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
