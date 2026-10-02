using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    internal Task<bool> ApplyTimedScaleAsync(
        IReadOnlyList<string> playerNames,
        double scale,
        TimeSpan duration,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>> dispatchInitialCommands,
        CancellationToken cancellationToken)
        => _timedPlayerScaleController.ApplyAsync(
            playerNames,
            scale,
            UsesModernAttributeIDs,
            UsesInlineTextComponentSyntax,
            duration,
            dispatchInitialCommands,
            cancellationToken);

    public Task SendTellrawAsync(string selector, string message, string color, bool bold, CancellationToken cancellationToken)
        => SendServerCommandAsync(
            MinecraftCommandBuilder.Tellraw(string.IsNullOrWhiteSpace(selector) ? "@a" : selector, message, color, bold, UsesInlineTextComponentSyntax),
            cancellationToken);

    public bool HasOtherPlayer(string excludedPlayerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(excludedPlayerName, out string excludedName))
            return false;

        lock (_playerGate)
        {
            int count = _knownPlayers.Count;
            return count > 1 ||
                count == 1 && !string.Equals(_knownPlayers[0], excludedName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public Task TellrawOthersAsync(ResolvedTarget target, string message, string color, bool bold, CancellationToken cancellationToken)
    {
        if (!MultiTargetingEnabled || target == null || string.IsNullOrWhiteSpace(message) || target.PlayerCount != 1)
            return Task.CompletedTask;

        if (!MinecraftNameHelper.TryNormalizePlayerName(target.MinecraftName, out string excludedName))
            return Task.CompletedTask;

        if (!HasOtherPlayer(excludedName))
            return Task.CompletedTask;

        string selector = MinecraftCommandBuilder.EveryoneExceptSelector(excludedName);
        return SendTellrawAsync(selector, message, color, bold, cancellationToken);
    }

    private static T GetRandom<T>(List<T> values) => values[Random.Shared.Next(values.Count)];

    public EffectDefinition GetRandomEffect() => GetRandom(_effectList);

    public string GetRandomLootTable() => GetRandom(_lootList);

    public string GetRandomMob() => GetRandom(_mobList);

    public string CurrentMinecraftVersion => _activeConfig?.Server.MinecraftVersion ?? string.Empty;

    private MinecraftVersionSupport.MinecraftVersionInfo GetMinecraftVersion()
        => _minecraftVersionInfo ?? MinecraftVersionSupport.GetVersion(CurrentMinecraftVersion);

    public bool UsesInlineTextComponentSyntax => GetMinecraftVersion().UsesInlineTextComponents;

    public bool UsesModernEntityAttributeNbt => GetMinecraftVersion().DataPackFormatMajor >= 48;
    public bool UsesNamespacedAttributeModifierIDs => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesModernAttributeIDs => GetMinecraftVersion().DataPackFormatMajor >= 57;

    public bool SupportsMaceEnchantments => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesFlattenedEnchantmentsComponent => GetMinecraftVersion().DataPackFormatMajor >= 71;

    public bool UsesNamespacedGameRules => GetMinecraftVersion().UsesNamespacedGameRules;

    public string MobLootGameRuleName => UsesNamespacedGameRules ? "minecraft:mob_drops" : "doMobLoot";

    internal bool IsPlayerOnline(string playerName) => (MinecraftProcessRunning || RCONConnected) && IsKnownPlayer(playerName);

    internal bool MinecraftProcessRunning => _minecraftSession.ProcessRunning;

    internal bool RCONConnected => _minecraftSession.RCONHealthy;

    public bool MinecraftServerReady => _minecraftSession.ServerReady && (!RemoteControlEnabled || _minecraftSession.RCONHealthy);
}
