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
            foreach (string player in _knownPlayers)
            {
                if (!string.Equals(player, excludedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
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

    public EffectDefinition GetRandomEffect()
    {
        List<EffectDefinition> effects = _effectList;
        return effects[Random.Shared.Next(effects.Count)];
    }

    public string GetRandomLootTable()
    {
        List<string> loot = _lootList;
        return loot[Random.Shared.Next(loot.Count)];
    }

    public string GetRandomMob()
    {
        List<string> mobs = _mobList;
        return mobs[Random.Shared.Next(mobs.Count)];
    }

    public string CurrentMinecraftVersion => _activeConfig?.Server.MinecraftVersion ?? string.Empty;

    private MinecraftVersionSupport.MinecraftVersionInfo GetMinecraftVersion()
        => _minecraftVersionInfo ?? MinecraftVersionSupport.GetVersion(CurrentMinecraftVersion);

    public bool UsesInlineTextComponentSyntax => GetMinecraftVersion().UsesInlineTextComponents;

    public bool UsesModernEntityAttributeNbt => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesModernAttributeIDs => GetMinecraftVersion().DataPackFormatMajor >= 57;

    public bool SupportsMaceEnchantments => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesFlattenedEnchantmentsComponent => GetMinecraftVersion().DataPackFormatMajor >= 71;

    public bool UsesNamespacedGameRules => GetMinecraftVersion().UsesNamespacedGameRules;

    public string MobLootGameRuleName => UsesNamespacedGameRules ? "minecraft:mob_drops" : "doMobLoot";

    internal bool MinecraftProcessRunning => _minecraftSession.ProcessRunning;

    internal bool RCONConnected => _minecraftSession.RCONHealthy;

    public bool MinecraftServerReady => _minecraftSession.ServerReady && (!RemoteControlEnabled || _minecraftSession.RCONHealthy);
}
