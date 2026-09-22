using System;
using System.IO;
using System.Text;

namespace TwitchCraft_V1.Setup;

internal static class DatapackInstaller
{
    private static readonly UTF8Encoding UTF8NoBOM = new(false);
    private const string DatapackName = "locateplayers";
    private const string EmbeddedDatapackPrefix = "TwitchCraft.locateplayers/";
    private const string DatapackDescription = "Locate players command for TwitchCraft";
    private const string DatapackWarningContext = "Locateplayers datapack installation failed; TwitchCraft will continue without it";

    private const string LegacyRunTellraw = "tellraw @s {\"text\":\"Players online:\",\"color\":\"yellow\",\"bold\":true}";
    private const string InlineRunTellraw = "tellraw @s {text:'Players online:',color:'yellow',bold:true}";

    private const string LegacyPrintTellraw = "tellraw @a[tag=lp_requester] [{\"selector\":\"@s\",\"color\":\"aqua\"},{\"text\":\": \",\"color\":\"gray\"},{\"text\":\"X=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$x\",\"objective\":\"lp_math\"}},{\"text\":\" Y=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$y\",\"objective\":\"lp_math\"}},{\"text\":\" Z=\",\"color\":\"gold\"},{\"score\":{\"name\":\"$z\",\"objective\":\"lp_math\"}},{\"text\":\" Dimension=\",\"color\":\"gray\"},{\"nbt\":\"Dimension\",\"entity\":\"@s\",\"color\":\"light_purple\"}]";
    private const string InlinePrintTellraw = "tellraw @a[tag=lp_requester,limit=1] [{selector:'@s',color:'aqua'},{text:': ',color:'gray'},{text:'X=',color:'gold'},{score:{name:'$x',objective:'lp_math'}},{text:' Y=',color:'gold'},{score:{name:'$y',objective:'lp_math'}},{text:' Z=',color:'gold'},{score:{name:'$z',objective:'lp_math'}},{text:' Dimension=',color:'gray'},{nbt:'Dimension',entity:'@s',color:'light_purple'}]";

    public static bool SyncLocateDatapack(TwitchCraftConfig config)
        => !config.Settings.MultiplayerEnabled ||
            SyncLocateDatapack(config.Server.ServerDirectory, config.Server.MinecraftVersion, ServerPropertyEditor.GetLevelName(config));

    public static bool SyncLocateDatapack(string serverDirectory, string minecraftVersion, string? levelName = null)
        => SyncLocateDatapack(serverDirectory, minecraftVersion, levelName, ReportWarning);

    internal static bool SyncLocateDatapack(
        string serverDirectory,
        string minecraftVersion,
        string? levelName,
        Action<string, Exception> reportWarning)
    {
        ArgumentNullException.ThrowIfNull(reportWarning);
        if (string.IsNullOrWhiteSpace(serverDirectory))
            return true;

        string destinationDirectory = Path.Combine(ServerPropertyEditor.GetWorldDirectory(serverDirectory, levelName), "datapacks", DatapackName);
        try
        {
            SyncEmbeddedFiles(destinationDirectory, minecraftVersion);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            reportWarning(DatapackWarningContext, ex);
            return false;
        }
    }

    private static void SyncEmbeddedFiles(string destinationDirectory, string minecraftVersion)
    {
        (string functionDirectoryName, bool rewriteCommands) = PrepareDestination(destinationDirectory, minecraftVersion);
        bool foundResource = false;

        foreach (string resourceName in typeof(DatapackInstaller).Assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(EmbeddedDatapackPrefix, StringComparison.Ordinal))
                continue;

            foundResource = true;
            string relativePath = resourceName[EmbeddedDatapackPrefix.Length..].Replace('\\', '/');
            if (functionDirectoryName == "function")
                relativePath = relativePath.Replace("/functions/", "/function/", StringComparison.Ordinal);

            string destinationPath = Path.Combine(destinationDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using Stream source = typeof(DatapackInstaller).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException("Embedded locateplayers datapack resource is missing: " + resourceName);
            using FileStream destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }

        if (!foundResource)
            throw new InvalidDataException("Embedded locateplayers datapack resources are missing.");
        if (rewriteCommands)
            RewriteCommands(destinationDirectory);
    }

    private static (string FunctionDirectoryName, bool RewriteCommands) PrepareDestination(string destinationDirectory, string minecraftVersion)
    {
        Directory.CreateDirectory(destinationDirectory);
        File.WriteAllText(Path.Combine(destinationDirectory, "pack.mcmeta"), BuildPackMetadata(minecraftVersion), UTF8NoBOM);

        string namespaceDirectory = Path.Combine(destinationDirectory, "data", DatapackName);
        string minecraftTagsDirectory = Path.Combine(destinationDirectory, "data", "minecraft", "tags");
        ResetDirectory(Path.Combine(namespaceDirectory, "functions"));
        ResetDirectory(Path.Combine(namespaceDirectory, "function"));
        ResetDirectory(Path.Combine(minecraftTagsDirectory, "functions"));
        ResetDirectory(Path.Combine(minecraftTagsDirectory, "function"));

        MinecraftVersionSupport.MinecraftVersionInfo version = MinecraftVersionSupport.GetVersion(minecraftVersion);
        return (version.UsesSingularFunctionDirectories ? "function" : "functions", version.UsesInlineTextComponents);
    }

    private static void ReportWarning(string context, Exception exception)
    {
        TwitchCraft_V1.ErrorHandling.LogNonFatal(context, exception);
        try
        {
            TwitchCraft_V1.ErrorHandling.ShowDatapackWarning(exception.Message);
        }
        catch (Exception popupException)
        {
            TwitchCraft_V1.ErrorHandling.LogNonFatal("Failed to show the locateplayers datapack warning", popupException);
        }
    }

    internal static string BuildPackMetadata(string minecraftVersion)
    {
        MinecraftVersionSupport.MinecraftVersionInfo version = MinecraftVersionSupport.GetVersion(minecraftVersion);
        if (version.UsesModernPackMetadata)
        {
            string exact = version.GetPackFormatJson();
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

    private static void RewriteCommands(string destinationDirectory)
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
                File.WriteAllText(filePath, rewritten, UTF8NoBOM);
        }
    }

    private static void ResetDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            ClearReadOnlyTree(path);
            Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to clear generated locateplayers datapack directory: " + path, ex);
        }
    }

    private static void ClearReadOnlyTree(string path)
    {
        DirectoryInfo root = new(path);
        ClearReadOnly(root);

        foreach (FileSystemInfo item in root.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            ClearReadOnly(item);
        }
    }

    private static void ClearReadOnly(FileSystemInfo item)
    {
        FileAttributes attributes = item.Attributes;
        if ((attributes & FileAttributes.ReadOnly) == 0)
            return;

        item.Attributes = attributes & ~FileAttributes.ReadOnly;
    }
}
