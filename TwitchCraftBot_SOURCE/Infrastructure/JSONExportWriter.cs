using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace TwitchCraftBot_V1;

internal static class JSONExportWriter
{
    public const string ExportsFolderName = "exports";
    public const string ViewingOnlyWarning = "These JSON files are readable exports from the SQLite database.";
    public const string EditingWarning = "Editing these JSON files will NOT affect TwitchCraft. To change real bot data, edit the .db files with a SQLite editor while TwitchCraft is shut down.";
    public const string SqliteEditorDownloadUrl = "https://sqlitebrowser.org/dl/";

    public static string GetExportDirectory(string dataPath)
    {
        string? directory = Path.GetDirectoryName(dataPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.CurrentDirectory;
        }

        return Path.Combine(directory, ExportsFolderName);
    }

    public static void WriteReadme(string exportDirectory)
    {
        string warningText =
            ViewingOnlyWarning + Environment.NewLine +
            EditingWarning + Environment.NewLine + Environment.NewLine +
            "Real editable data files:" + Environment.NewLine +
            "- viewer_tokens.db" + Environment.NewLine +
            "- statistics.db" + Environment.NewLine + Environment.NewLine +
            "SQLite viewer/editor download:" + Environment.NewLine +
            SqliteEditorDownloadUrl + Environment.NewLine;

        string READMEPath = Path.Combine(exportDirectory, "READ_ME_FIRST.txt");
        string tempPath = FileSystemHelper.GetUniqueTempPath(READMEPath);
        File.WriteAllText(tempPath, warningText, Encoding.UTF8);
        ReplaceFile(tempPath, READMEPath);
    }

    public static void WriteJsonAtomic(string path, Action<JsonTextWriter> writeBody)
    {
        ArgumentNullException.ThrowIfNull(writeBody);

        FileSystemHelper.EnsureParentDir(path);

        string tempPath = FileSystemHelper.GetUniqueTempPath(path);
        using (StreamWriter streamWriter = new(tempPath, false, Encoding.UTF8))
        using (JsonTextWriter jsonWriter = new(streamWriter) { Formatting = Formatting.Indented })
        {
            writeBody(jsonWriter);
        }

        ReplaceFile(tempPath, path);
    }

    public static void WriteExportStart(JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("ViewingOnly");
        writer.WriteValue(ViewingOnlyWarning);
        writer.WritePropertyName("EditingWarning");
        writer.WriteValue(EditingWarning);
        writer.WriteWhitespace(Environment.NewLine);
        writer.WritePropertyName("SQLiteViewerEditorDownload");
        writer.WriteValue(SqliteEditorDownloadUrl);
        writer.WritePropertyName("ExportedAtUtc");
        writer.WriteValue(DateTime.UtcNow.ToString("O"));
    }

    public static void WriteExportEnd(JsonWriter writer)
    {
        writer.WriteEndObject();
    }

    public static void WriteSectionBreak(JsonWriter writer)
    {
        writer.WriteWhitespace(Environment.NewLine);
    }

    public static void WriteCount(JsonWriter writer, string name, long value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(Math.Max(0L, value));
    }

    private static void ReplaceFile(string tempPath, string path)
        => FileSystemHelper.ReplaceFile(tempPath, path, backupPath: null, "Atomic JSON export save failed; falling back to copy");
}
