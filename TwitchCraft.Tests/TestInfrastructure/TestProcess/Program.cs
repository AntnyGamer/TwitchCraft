using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

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
bool probeMode = string.Equals(mode, "ready-probes", StringComparison.Ordinal);
await File.WriteAllLinesAsync(jarPath + ".args", args);
await File.WriteAllTextAsync(jarPath + ".pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

if (string.Equals(mode, "exit-immediately", StringComparison.Ordinal))
    return 42;

if (string.Equals(mode, "ready", StringComparison.Ordinal) || probeMode)
    await WriteOutputAsync("Done (0.500s)! For help, type \"help\"");

await using FileStream commandLog = new(
    jarPath + ".stdin",
    FileMode.Append,
    FileAccess.Write,
    FileShare.ReadWrite);
await using StreamWriter commandWriter = new(commandLog);

string probeMarker = string.Empty;
while (await Console.In.ReadLineAsync() is string line)
{
    await commandWriter.WriteLineAsync(line);
    await commandWriter.FlushAsync();

    if (probeMode)
        await HandleProbeCommandAsync(line);

    if (string.Equals(line, "stop", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(mode, "ignore-stop", StringComparison.Ordinal))
    {
        return 0;
    }
}

return 0;

async Task HandleProbeCommandAsync(string command)
{
    const string markerSetPrefix = "data modify storage twitchcraft:probe marker set value \"";
    if (command.StartsWith(markerSetPrefix, StringComparison.Ordinal))
    {
        int end = command.LastIndexOf('"');
        probeMarker = end > markerSetPrefix.Length
            ? command[markerSetPrefix.Length..end]
            : string.Empty;
        return;
    }

    if (string.Equals(command, "data get storage twitchcraft:probe marker", StringComparison.Ordinal))
    {
        if (probeMarker.Length > 0)
            await WriteOutputAsync("Storage twitchcraft:probe contains \"" + probeMarker + "\"");
        return;
    }

    if (string.Equals(command, "list", StringComparison.Ordinal))
    {
        await DelayProbeAsync();
        List<(string Name, int GameMode)> players = ReadPlayers();
        await WriteOutputAsync(
            "There are " + players.Count.ToString(CultureInfo.InvariantCulture) +
            " of a max of 20 players online: " +
            string.Join(", ", players.ConvertAll(static player => player.Name)));
        return;
    }

    if (command.Contains("playerGameType", StringComparison.Ordinal))
    {
        await DelayProbeAsync();
        foreach ((string name, int gameMode) in ReadPlayers())
            await WriteOutputAsync(name + " has the following entity data: " + gameMode.ToString(CultureInfo.InvariantCulture));
        return;
    }

    string player = GetSelectedPlayer(command);
    if (command.StartsWith("attribute ", StringComparison.Ordinal) &&
        command.Contains("max_health", StringComparison.OrdinalIgnoreCase) &&
        command.EndsWith(" get", StringComparison.Ordinal))
    {
        await DelayProbeAsync();
        await WriteOutputAsync(
            "Value of attribute minecraft:max_health for entity " + player + " is " +
            ReadSidecar(".health", "20"));
        return;
    }

    if (command.StartsWith("data get entity ", StringComparison.Ordinal) &&
        (command.EndsWith(" attributes", StringComparison.Ordinal) ||
         command.EndsWith(" Attributes", StringComparison.Ordinal)))
    {
        await DelayProbeAsync();
        await WriteOutputAsync(player + " has the following entity data: " + ReadSidecar(".attributes", "[]"));
        return;
    }

    if (command.StartsWith("data get entity ", StringComparison.Ordinal) &&
        command.EndsWith(" SelectedItem", StringComparison.Ordinal))
    {
        await DelayProbeAsync();
        await WriteOutputAsync(
            player + " has the following entity data: " +
            ReadSidecar(".item", "{id:'minecraft:diamond_sword',count:1}"));
    }
}

List<(string Name, int GameMode)> ReadPlayers()
{
    string path = jarPath + ".players";
    if (!File.Exists(path))
        return [("streamer", 0)];

    List<(string Name, int GameMode)> players = [];
    foreach (string line in File.ReadAllLines(path))
    {
        string[] parts = line.Split('|', 2);
        if (parts.Length == 2 &&
            parts[0].Length > 0 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameMode))
        {
            players.Add((parts[0], gameMode));
        }
    }
    return players;
}

string GetSelectedPlayer(string command)
{
    const string marker = "name=\"";
    int start = command.IndexOf(marker, StringComparison.Ordinal);
    if (start >= 0)
    {
        start += marker.Length;
        int end = command.IndexOf('"', start);
        if (end > start)
            return command[start..end];
    }

    List<(string Name, int GameMode)> players = ReadPlayers();
    return players.Count == 0 ? "streamer" : players[0].Name;
}

string ReadSidecar(string suffix, string fallback)
{
    string path = jarPath + suffix;
    return File.Exists(path) ? File.ReadAllText(path).Trim() : fallback;
}

async Task DelayProbeAsync()
{
    string delay = ReadSidecar(".probe-delay-ms", "0");
    if (int.TryParse(delay, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds) && milliseconds > 0)
        await Task.Delay(milliseconds);
}

async Task WriteOutputAsync(string message)
{
    await Console.Out.WriteLineAsync("[Server thread/INFO]: " + message);
    await Console.Out.FlushAsync();
}
