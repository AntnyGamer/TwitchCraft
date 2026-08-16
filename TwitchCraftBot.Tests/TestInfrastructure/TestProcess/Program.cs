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
await File.WriteAllLinesAsync(jarPath + ".args", args);
await File.WriteAllTextAsync(jarPath + ".pid", Environment.ProcessId.ToString());

if (string.Equals(mode, "exit-immediately", StringComparison.Ordinal))
    return 42;

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
}

return 0;
