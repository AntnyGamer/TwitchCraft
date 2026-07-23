using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    public Task SendTellrawAsync(string selector, string message, string color, bool bold, CancellationToken cancellationToken)
        => SendServerCommandAsync(
            MinecraftCommandBuilder.Tellraw(string.IsNullOrWhiteSpace(selector) ? "@a" : selector, message, color, bold, UsesInlineTextComponentSyntax),
            cancellationToken);

    public bool HasOtherKnownPlayer(string excludedPlayerName)
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

    public Task SendTellrawToOthersAsync(ResolvedTarget target, string message, string color, bool bold, CancellationToken cancellationToken)
    {
        if (!MultiTargetingEnabled || target == null || string.IsNullOrWhiteSpace(message) || target.PlayerCount != 1)
            return Task.CompletedTask;

        if (!MinecraftNameHelper.TryNormalizePlayerName(target.MinecraftName, out string excludedName))
            return Task.CompletedTask;

        if (!HasOtherKnownPlayer(excludedName))
            return Task.CompletedTask;

        string selector = MinecraftCommandBuilder.AllExceptPlayerSelector(excludedName);
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

    private bool TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo versionInfo)
    {
        string version = CurrentMinecraftVersion;
        if (!string.Equals(_cachedMinecraftFeatureVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            _cachedMinecraftFeatureInfo = MinecraftVersionSupport.TryGetVersion(version, out MinecraftVersionSupport.MinecraftVersionInfo resolved)
                ? resolved
                : null;
            _cachedMinecraftFeatureVersion = version;
        }

        if (_cachedMinecraftFeatureInfo != null)
        {
            versionInfo = _cachedMinecraftFeatureInfo;
            return true;
        }

        versionInfo = null!;
        return false;
    }

    public bool UsesItemComponentsSyntax
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesItemComponents;
        }
    }

    public bool UsesInlineTextComponentSyntax
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesInlineTextComponents;
        }
    }

    public bool UsesModernEntityAttributeNbt
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.DataPackFormatMajor >= 48;
        }
    }

    public bool UsesNamespacedGameRules
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesNamespacedGameRules;
        }
    }

    public string MobLootGameRuleName => UsesNamespacedGameRules ? "minecraft:mob_drops" : "doMobLoot";

    public bool MinecraftServerReady => _minecraftServerReady;

}
