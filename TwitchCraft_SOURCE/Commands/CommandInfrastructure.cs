using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

// ===== Shared command models =====

internal static class CommandRandom
{
    internal static int Next(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum);

    internal static int Next(int minimum, int exclusiveMaximum) => RandomNumberGenerator.GetInt32(minimum, exclusiveMaximum);

    internal static bool Chance(double probability)
        => probability >= 1 || (probability > 0 && RandomNumberGenerator.GetInt32(int.MaxValue) < probability * int.MaxValue);
}

public sealed class EffectDefinition
{
    public string ID { get; set; } = string.Empty;
    public int MinSeconds { get; set; }
    public int MaxSeconds { get; set; }
    public int MinAmplifier { get; set; }
    public int MaxAmplifier { get; set; }
}

public delegate Task ChatCommandHandler(string[] args, string sender, CancellationToken cancellationToken);

public sealed class ResolvedTarget
{
    public string Selector { get; set; } = "@a";
    public string DisplayName { get; set; } = "everyone";
    public string MinecraftName { get; set; } = string.Empty;
    public int PlayerCount { get; set; } = 1;
    public bool DefaultPlayerInclusionKnown { get; set; }
    public bool IncludesDefaultMinecraftPlayer { get; set; }
    public List<string>? TargetablePlayers { get; set; }
}

// ===== Command registry =====

[Flags]
public enum ChatCommandStatisticFlags
{
    None = 0,
    GameAffecting = 1,
    Dangerous = 2,
    Nice = 4
}

public sealed class ChatCommandRegistry(
    Dictionary<string, ChatCommandHandler>? handlers,
    Dictionary<string, ChatCommandStatisticFlags>? statisticFlags = null)
{
    private readonly Dictionary<string, ChatCommandHandler> _handlers = handlers ?? new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatCommandStatisticFlags> _statisticFlags = statisticFlags ?? new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string>? _commandNames;

    public bool TryResolve(string? name, out ChatCommandHandler handler)
        => _handlers.TryGetValue(name ?? string.Empty, out handler!);

    public ChatCommandStatisticFlags GetStatisticFlags(string? name)
        => _statisticFlags.TryGetValue(name ?? string.Empty, out ChatCommandStatisticFlags flags)
            ? flags
            : ChatCommandStatisticFlags.None;

    public IReadOnlyList<string> CommandNames
    {
        get
        {
            if (_commandNames != null)
            {
                return _commandNames;
            }

            string[] names = [.. _handlers.Keys];
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return _commandNames = Array.AsReadOnly(names);
        }
    }

    public static ChatCommandRegistry CreateDefault(MainHandler runtime)
    {
        var statisticFlags = new Dictionary<string, ChatCommandStatisticFlags>(64, StringComparer.OrdinalIgnoreCase);
        return new(CommandList.BuildCommands(runtime, statisticFlags), statisticFlags);
    }
}
