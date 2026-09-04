using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
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
            UsesModernAttributeIds,
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
        string version = CurrentMinecraftVersion;
        List<EffectDefinition> availableEffects;
        lock (_effectCacheGate)
        {
            if (!string.Equals(_cachedSupportedEffectsVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                List<EffectDefinition> effects = new(_effectList.Count);
                foreach (EffectDefinition effect in _effectList)
                {
                    if (MinecraftVersionSupport.SupportsStatusEffect(version, effect.ID))
                    {
                        effects.Add(effect);
                    }
                }

                _cachedSupportedEffects = effects.Count == 0 ? _effectList : effects;
                _cachedSupportedEffectsVersion = version;
            }

            availableEffects = _cachedSupportedEffects;
        }

        return availableEffects[Random.Shared.Next(availableEffects.Count)];
    }

    public string GetRandomLootTable() => _lootList[Random.Shared.Next(_lootList.Count)];

    public string GetRandomMob() => _mobList[Random.Shared.Next(_mobList.Count)];

    public string CurrentMinecraftVersion => _currentMinecraftVersion;

    private MinecraftVersionSupport.MinecraftVersionInfo GetMinecraftVersion()
    {
        string version = CurrentMinecraftVersion;
        if (!string.Equals(_cachedMinecraftFeatureVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            _cachedMinecraftFeatureInfo = MinecraftVersionSupport.GetVersion(version);
            _cachedMinecraftFeatureVersion = version;
        }

        return _cachedMinecraftFeatureInfo
            ?? throw new InvalidOperationException("Minecraft version information is unavailable.");
    }

    public bool UsesInlineTextComponentSyntax => GetMinecraftVersion().UsesInlineTextComponents;

    public bool UsesModernEntityAttributeNbt => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesModernAttributeIds => GetMinecraftVersion().DataPackFormatMajor >= 57;

    public bool SupportsMaceEnchantments => GetMinecraftVersion().DataPackFormatMajor >= 48;

    public bool UsesFlattenedEnchantmentsComponent => GetMinecraftVersion().DataPackFormatMajor >= 71;

    public bool UsesNamespacedGameRules => GetMinecraftVersion().UsesNamespacedGameRules;

    public string MobLootGameRuleName => UsesNamespacedGameRules ? "minecraft:mob_drops" : "doMobLoot";

    public bool MinecraftServerReady => _minecraftServerReady;
}
