using System;
using System.Collections.Generic;
using System.Globalization;

namespace TwitchCraft_V1.Setup;

internal static class MinecraftVersionSupport
{
    private static readonly string[] AdditionalMobs =
    [
        "armadillo",
        "bogged",
        "breeze",
        "creaking",
        "ghastling",
        "happy_ghast",
        "copper_golem",
        "nautilus",
        "zombie_nautilus",
        "camel_husk",
        "parched",
        "sulfur_cube"
    ];

    private static readonly string[] AdditionalLootTables =
    [
        "chests/trial_chambers/corridor",
        "chests/trial_chambers/entrance",
        "chests/trial_chambers/intersection",
        "chests/trial_chambers/intersection_barrel",
        "chests/trial_chambers/reward",
        "chests/trial_chambers/reward_common",
        "chests/trial_chambers/reward_ominous",
        "chests/trial_chambers/reward_ominous_common",
        "chests/trial_chambers/reward_ominous_rare",
        "chests/trial_chambers/reward_ominous_unique",
        "chests/trial_chambers/reward_rare",
        "chests/trial_chambers/reward_unique",
        "chests/trial_chambers/supply",
        "barrels/abandoned_camp_barrel",
        "chests/abandoned_camp_common_chest",
        "chests/abandoned_camp_secret_chest"
    ];

    private static readonly MinecraftVersionInfo[] Versions =
    [
        new("1.20.5", "", 21, 41, 0, false, false, false, false, 1, 0),
        new("1.20.6", "", 21, 41, 0, false, false, false, false, 1, 0),
        new("1.21.0", "", 21, 48, 0, true, false, false, false, 3, 13), // 13 = Trial Chambers loot tables only
        new("1.21.1", "", 21, 48, 0, true, false, false, false, 3, 13),
        new("1.21.2", "", 21, 57, 0, true, false, false, false, 3, 13),
        new("1.21.3", "", 21, 57, 0, true, false, false, false, 3, 13),
        new("1.21.4", "", 21, 61, 0, true, false, false, false, 4, 13),
        new("1.21.5", "", 21, 71, 0, true, true, false, false, 4, 13),
        new("1.21.6", "", 21, 80, 0, true, true, false, false, 6, 13),
        new("1.21.7", "", 21, 81, 0, true, true, false, false, 6, 13),
        new("1.21.8", "", 21, 81, 0, true, true, false, false, 6, 13),
        new("1.21.9", "", 21, 88, 0, true, true, true, false, 7, 13),
        new("1.21.10", "", 21, 88, 0, true, true, true, false, 7, 13),
        new("1.21.11", "", 21, 94, 1, true, true, true, true, 11, 13),
        new("26.1", "26.1.0", 25, 101, 1, true, true, true, true, 11, 13),
        new("26.1.1", "", 25, 101, 1, true, true, true, true, 11, 13),
        new("26.1.2", "", 25, 101, 1, true, true, true, true, 11, 13),
        new("26.2", "26.2.0", 25, 107, 1, true, true, true, true, 12, 13),
        new("26.3", "26.3.0", 25, 121, 0, true, true, true, true, 12, 16) // 16 = Trial Chambers + Abandoned Camp loot tables
    ];

    private static readonly Dictionary<string, MinecraftVersionInfo> VersionMap = BuildVersionMap();

    public static IReadOnlyList<MinecraftVersionInfo> SupportedVersions { get; } = Versions;

    public static bool TryGetVersion(string? versionID, out MinecraftVersionInfo version)
    {
        string normalized = versionID?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            version = null!;
            return false;
        }

        return VersionMap.TryGetValue(normalized, out version!);
    }

    public static MinecraftVersionInfo GetVersion(string? versionID)
    {
        if (TryGetVersion(versionID, out MinecraftVersionInfo version))
            return version;

        string display = string.IsNullOrWhiteSpace(versionID) ? "(not specified)" : versionID.Trim();
        throw new NotSupportedException("Minecraft version '" + display + "' is not supported by this TwitchCraft build.");
    }

    public static bool SupportsStatusEffect(string versionID, string? effectID)
    {
        string normalizedEffectID = effectID?.Trim() ?? string.Empty;
        if (normalizedEffectID.Length == 0)
            return false;

        MinecraftVersionInfo version = GetVersion(versionID);
        return normalizedEffectID switch
        {
            "infested" or "trial_omen" => version.DataPackFormatMajor >= 48,
            _ => true
        };
    }

    private static Dictionary<string, MinecraftVersionInfo> BuildVersionMap()
    {
        Dictionary<string, MinecraftVersionInfo> map = new(Versions.Length, StringComparer.OrdinalIgnoreCase);

        foreach (MinecraftVersionInfo version in Versions)
        {
            map[version.ID] = version;
            if (version.DisplayID != version.ID)
                map[version.DisplayID] = version;
        }

        return map;
    }

    internal sealed class MinecraftVersionInfo(
        string ID,
        string alias,
        int requiredJDK,
        int dataPackFormatMajor,
        int dataPackFormatMinor,
        bool usesSingularFunctionDirectories,
        bool usesInlineTextComponents,
        bool usesModernPackMetadata,
        bool usesNamespacedGameRules,
        int additionalMobCount,
        int additionalLootTableCount)
    {
        public string ID { get; } = ID;
        public string DisplayID { get; } = alias.Length > 0 ? alias : ID;
        public int RequiredJDK { get; } = requiredJDK;
        public int DataPackFormatMajor { get; } = dataPackFormatMajor;
        public int DataPackFormatMinor { get; } = dataPackFormatMinor;
        public bool UsesSingularFunctionDirectories { get; } = usesSingularFunctionDirectories;
        public bool UsesInlineTextComponents { get; } = usesInlineTextComponents;
        public bool UsesModernPackMetadata { get; } = usesModernPackMetadata;
        public bool UsesNamespacedGameRules { get; } = usesNamespacedGameRules;
        public bool UsesServerSettingGameRules => DataPackFormatMajor >= 88;
        public ReadOnlySpan<string> AdditionalMobIDs => AdditionalMobs.AsSpan(0, additionalMobCount);
        public ReadOnlySpan<string> AdditionalLootTableIDs => AdditionalLootTables.AsSpan(0, additionalLootTableCount);

        public string GetPackFormatJson()
        {
            if (DataPackFormatMinor > 0 || UsesModernPackMetadata)
                return "[" + DataPackFormatMajor.ToString(CultureInfo.InvariantCulture) + ", " + DataPackFormatMinor.ToString(CultureInfo.InvariantCulture) + "]";

            return DataPackFormatMajor.ToString(CultureInfo.InvariantCulture);
        }
    }
}
