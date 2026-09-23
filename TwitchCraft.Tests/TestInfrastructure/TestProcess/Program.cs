using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

static string ReadState(string jarPath, string suffix, string fallback)
{
    string path = jarPath + suffix;
    return File.Exists(path) ? File.ReadAllText(path).Trim() : fallback;
}

static string[] ReadNames(string jarPath, string suffix)
    => ReadState(jarPath, suffix, string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static bool ConsumeCount(string path)
{
    if (!int.TryParse(ReadState(path, string.Empty, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
        return false;

    File.WriteAllText(path, (count - 1).ToString(CultureInfo.InvariantCulture));
    return true;
}

static string GetTargetPlayer(string command)
{
    const string marker = "name=\"";
    int start = command.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0)
        return string.Empty;
    start += marker.Length;
    int end = command.IndexOf('"', start);
    return end > start ? command[start..end] : string.Empty;
}

static async Task WriteOutputAsync(string line)
{
    await Console.Out.WriteLineAsync(line);
    await Console.Out.FlushAsync();
}

string? jarPath = null;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (string.Equals(args[i], "-jar", StringComparison.Ordinal))
    {
        jarPath = args[i + 1];
        break;
    }
}

if (string.IsNullOrWhiteSpace(jarPath))
    return 2;

string mode = File.Exists(jarPath)
    ? (await File.ReadAllTextAsync(jarPath)).Trim()
    : string.Empty;
bool responsive = string.Equals(mode, "ready-responsive", StringComparison.Ordinal);
await File.WriteAllLinesAsync(jarPath + ".args", args);
await File.WriteAllTextAsync(jarPath + ".pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

if (string.Equals(mode, "exit-immediately", StringComparison.Ordinal))
    return 42;

if (mode.StartsWith("ready", StringComparison.Ordinal))
{
    await Console.Out.WriteLineAsync("[Server thread/INFO]: Done (0.500s)! For help, type \"help\"");
    if (responsive)
    {
        string[] players = ReadNames(jarPath, ".players");
        await Console.Out.WriteLineAsync(
            "There are " + players.Length.ToString(CultureInfo.InvariantCulture) +
            " of a max of 20 players online: " + string.Join(", ", players));
    }
    await Console.Out.FlushAsync();
}

await using FileStream commandLog = new(
    jarPath + ".stdin",
    FileMode.Append,
    FileAccess.Write,
    FileShare.ReadWrite);
await using StreamWriter commandWriter = new(commandLog);

while (await Console.In.ReadLineAsync() is string line)
{
    await commandWriter.WriteLineAsync(line);
    await commandWriter.FlushAsync();

    if (string.Equals(line, "stop", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(mode, "ignore-stop", StringComparison.Ordinal))
    {
        return 0;
    }

    if (!responsive)
        continue;

    if (line.StartsWith("data get storage twitchcraft:tc_probe_", StringComparison.Ordinal))
    {
        if (!ConsumeCount(jarPath + ".drop-marker-responses"))
            await WriteOutputAsync("Storage " + line["data get storage ".Length..] + " has the following contents: {}");
        continue;
    }

    if (string.Equals(line, "list", StringComparison.Ordinal))
    {
        string[] players = ReadNames(jarPath, ".players");
        await WriteOutputAsync(
            "There are " + players.Length.ToString(CultureInfo.InvariantCulture) +
            " of a max of 20 players online: " + string.Join(", ", players));
        continue;
    }

    if (string.Equals(line, "execute as @a run data get entity @s playerGameType", StringComparison.Ordinal))
    {
        string[] players = ReadNames(jarPath, ".players");
        string[] spectators = ReadNames(jarPath, ".spectators");
        foreach (string player in players)
        {
            bool spectator = Array.Exists(spectators, value => string.Equals(value, player, StringComparison.OrdinalIgnoreCase));
            await WriteOutputAsync(player + " has the following entity data: " + (spectator ? "3" : "0"));
        }
        continue;
    }

    string targetPlayer = GetTargetPlayer(line);
    if (targetPlayer.Length == 0)
        continue;

    if (line.StartsWith("attribute ", StringComparison.Ordinal) && line.EndsWith(" get", StringComparison.Ordinal))
    {
        if (!ConsumeCount(jarPath + ".drop-probe-responses"))
        {
            string health = ReadState(jarPath, ".health", "20");
            await WriteOutputAsync("Value of attribute Max Health for entity " + targetPlayer + " is " + health);
        }
        continue;
    }

    if (line.EndsWith(" SelectedItem", StringComparison.Ordinal))
    {
        if (int.TryParse(ReadState(jarPath, ".probe-delay", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay) && delay > 0)
            await Task.Delay(delay);
        if (!ConsumeCount(jarPath + ".drop-probe-responses"))
        {
            string item = ReadState(
                jarPath,
                ".item." + targetPlayer.ToLowerInvariant(),
                ReadState(jarPath, ".item", "{id:'minecraft:air',count:1}"));
            await WriteOutputAsync(targetPlayer + " has the following entity data: " + item);
        }
        continue;
    }

    if ((line.EndsWith(" attributes", StringComparison.Ordinal) || line.EndsWith(" Attributes", StringComparison.Ordinal)) &&
        !ConsumeCount(jarPath + ".drop-probe-responses"))
    {
        await WriteOutputAsync(targetPlayer + " has the following entity data: " + ReadState(jarPath, ".attributes", "[]"));
    }
}

return 0;
