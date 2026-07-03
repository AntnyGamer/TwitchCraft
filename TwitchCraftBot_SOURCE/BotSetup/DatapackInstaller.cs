using System;
using System.IO;
using System.Text;

namespace TwitchCraftBot_V1.BotSetup;

internal static class DatapackInstaller
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private const string DatapackName = "locateplayers";
    private const string DatapackDescription = "Locate players command for TwitchCraft";
    private const int MinLegacyPackFormat = 15;
    private const int MaxLegacyPackFormat = 61;

    private const string LegacyRunTellraw = "tellraw @s {\"text\":\"Players online:\",\"color\":\"yellow\",\"bold\":true}";
    private const string InlineRunTellraw = "tellraw @s {text:'Players online:',color:'yellow',bold:true}";

    private const string LegacyPrintTellraw = "tellraw @a[tag=lp_requester] [{\"selector\":\"@s\",\"color\":\"aqua\"},{\"text\":\": \",\"color\":\"gray\"},{\"text\":\"X=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$x\",\"objective\":\"lp_math\"}},{\"text\":\" Y=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$y\",\"objective\":\"lp_math\"}},{\"text\":\" Z=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$z\",\"objective\":\"lp_math\"}}]";
    private const string InlinePrintTellraw = "tellraw @a[tag=lp_requester,limit=1] [{selector:'@s',color:'aqua'},{text:': ',color:'gray'},{text:'X=',color:'gold'},{score:{name:'$x',objective:'lp_math'}},{text:' Y=',color:'gold'},{score:{name:'$y',objective:'lp_math'}},{text:' Z=',color:'gold'},{score:{name:'$z',objective:'lp_math'}}]";

    public static void SyncLocatePlayersDatapack(BotConfig config)
    {
        if (config.Settings.MultiplayerEnabled)
            SyncLocatePlayersDatapack(config.Server.ServerDirectory, config.Server.MinecraftVersion, ServerPropertyEditor.GetLevelName(config));
    }

    public static void SyncLocatePlayersDatapack(string serverDirectory, string? minecraftVersion = null, string? levelName = null)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
            return;

        string sourceDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", DatapackName);
        if (!Directory.Exists(sourceDirectory))
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Locateplayers datapack source folder is missing: " + sourceDirectory, null);
            return;
        }

        string destinationDirectory = Path.Combine(ServerPropertyEditor.GetWorldDirectory(serverDirectory, levelName), "datapacks", DatapackName);
        SyncLocatePlayersDatapackFiles(sourceDirectory, destinationDirectory, minecraftVersion);
    }

    private static void SyncLocatePlayersDatapackFiles(string sourceDirectory, string destinationDirectory, string? minecraftVersion)
    {
        string functionSource = Path.Combine(sourceDirectory, "data", DatapackName, "functions");
        string tagSource = Path.Combine(sourceDirectory, "data", "minecraft", "tags", "functions");
        if (!Directory.Exists(functionSource) || !Directory.Exists(tagSource))
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Locateplayers datapack source folder is incomplete: " + sourceDirectory, null);
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        File.WriteAllText(Path.Combine(destinationDirectory, "pack.mcmeta"), BuildPackMetadataJson(minecraftVersion), Utf8NoBom);

        string namespaceDirectory = Path.Combine(destinationDirectory, "data", DatapackName);
        string minecraftTagsDirectory = Path.Combine(destinationDirectory, "data", "minecraft", "tags");

        ResetDirectory(Path.Combine(namespaceDirectory, "functions"));
        ResetDirectory(Path.Combine(namespaceDirectory, "function"));
        ResetDirectory(Path.Combine(minecraftTagsDirectory, "functions"));
        ResetDirectory(Path.Combine(minecraftTagsDirectory, "function"));

        bool versionKnown = MinecraftVersionSupport.TryGetVersion(minecraftVersion, out MinecraftVersionSupport.MinecraftVersionInfo version);
        bool installLegacyLayout = !versionKnown || !version.UsesSingularFunctionDirectories;
        bool installNewLayout = !versionKnown || version.UsesSingularFunctionDirectories;

        if (installLegacyLayout)
        {
            TwitchCraftBot_V1.FileSystemHelper.CopyDirectory(functionSource, Path.Combine(namespaceDirectory, "functions"), skipReparsePoints: true);
            TwitchCraftBot_V1.FileSystemHelper.CopyDirectory(tagSource, Path.Combine(minecraftTagsDirectory, "functions"), skipReparsePoints: true);
        }

        if (installNewLayout)
        {
            TwitchCraftBot_V1.FileSystemHelper.CopyDirectory(functionSource, Path.Combine(namespaceDirectory, "function"), skipReparsePoints: true);
            TwitchCraftBot_V1.FileSystemHelper.CopyDirectory(tagSource, Path.Combine(minecraftTagsDirectory, "function"), skipReparsePoints: true);
        }

        if (versionKnown && version.UsesInlineTextComponents)
            RewriteLocatePlayersTextCommands(destinationDirectory);
    }

    private static string BuildPackMetadataJson(string? minecraftVersion)
    {
        if (MinecraftVersionSupport.TryGetVersion(minecraftVersion, out MinecraftVersionSupport.MinecraftVersionInfo version))
        {
            if (version.UsesModernPackMetadata)
            {
                string exact = version.GetExactPackFormatJsonValue();
                return "{\n"
                     + "  \"pack\": {\n"
                     + "    \"min_format\": " + exact + ",\n"
                     + "    \"max_format\": " + exact + ",\n"
                     + "    \"description\": \"" + DatapackDescription + "\"\n"
                     + "  }\n"
                     + "}";
            }

            return "{\n"
                 + "  \"pack\": {\n"
                 + "    \"pack_format\": " + version.DataPackFormatMajor + ",\n"
                 + "    \"description\": \"" + DatapackDescription + "\"\n"
                 + "  }\n"
                 + "}";
        }

        return "{\n"
             + "  \"pack\": {\n"
             + "    \"pack_format\": " + MinLegacyPackFormat + ",\n"
             + "    \"supported_formats\": {\n"
             + "      \"min_inclusive\": " + MinLegacyPackFormat + ",\n"
             + "      \"max_inclusive\": " + MaxLegacyPackFormat + "\n"
             + "    },\n"
             + "    \"description\": \"" + DatapackDescription + "\"\n"
             + "  }\n"
             + "}";
    }

    private static void RewriteLocatePlayersTextCommands(string destinationDirectory)
    {
        foreach (string filePath in Directory.EnumerateFiles(destinationDirectory, "*.mcfunction", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(filePath);
            bool isRunFile = string.Equals(fileName, "run.mcfunction", StringComparison.OrdinalIgnoreCase);
            bool isPrintFile = string.Equals(fileName, "print_one.mcfunction", StringComparison.OrdinalIgnoreCase);
            if (!isRunFile && !isPrintFile)
                continue;

            string content = File.ReadAllText(filePath, Encoding.UTF8);
            string rewritten = isRunFile
                ? content.Replace(LegacyRunTellraw, InlineRunTellraw, StringComparison.Ordinal)
                : content.Replace(LegacyPrintTellraw, InlinePrintTellraw, StringComparison.Ordinal);

            if (!string.Equals(content, rewritten, StringComparison.Ordinal))
                File.WriteAllText(filePath, rewritten, Utf8NoBom);
        }
    }

    private static void ResetDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            ClearReadOnlyAttributes(path);
            Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to clear generated locateplayers datapack directory: " + path, ex);
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        DirectoryInfo root = new(path);
        ClearReadOnlyAttribute(root);

        foreach (FileSystemInfo item in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            ClearReadOnlyAttribute(item);
        }
    }

    private static void ClearReadOnlyAttribute(FileSystemInfo item)
    {
        FileAttributes attributes = item.Attributes;
        if ((attributes & FileAttributes.ReadOnly) == 0)
            return;

        item.Attributes = attributes & ~FileAttributes.ReadOnly;
    }

}
