using System;
using System.Collections.Generic;
using System.Globalization;

namespace TwitchCraftBot_V1.BotSetup;

internal static class MinecraftVersionSupport
{
    private static readonly string[] TrialChamberLootTables =
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
        "chests/trial_chambers/supply"
    ];

    private static readonly MinecraftVersionInfo[] Versions =
    [
        new("1.20", ["1.20.0"], 17, 15, 0, false, false, false, false, false, [], []),
        new("1.20.1", [], 17, 15, 0, false, false, false, false, false, [], []),
        new("1.20.2", [], 17, 18, 0, false, false, false, false, false, [], []),
        new("1.20.3", [], 17, 26, 0, false, false, false, false, false, [], []),
        new("1.20.4", [], 17, 26, 0, false, false, false, false, false, [], []),
        new("1.20.5", [], 21, 41, 0, false, true, false, false, false, ["armadillo"], []),
        new("1.20.6", [], 21, 41, 0, false, true, false, false, false, ["armadillo"], []),
        new("1.21.0", [], 21, 48, 0, true, true, false, false, false, ["armadillo", "bogged", "breeze"], TrialChamberLootTables),
        new("1.21.1", [], 21, 48, 0, true, true, false, false, false, ["armadillo", "bogged", "breeze"], TrialChamberLootTables),
        new("1.21.2", [], 21, 57, 0, true, true, false, false, false, ["armadillo", "bogged", "breeze"], TrialChamberLootTables),
        new("1.21.3", [], 21, 57, 0, true, true, false, false, false, ["armadillo", "bogged", "breeze"], TrialChamberLootTables),
        new("1.21.4", [], 21, 61, 0, true, true, false, false, false, ["armadillo", "bogged", "breeze", "creaking"], TrialChamberLootTables),
        new("1.21.5", [], 21, 71, 0, true, true, true, false, false, ["armadillo", "bogged", "breeze", "creaking"], TrialChamberLootTables),
        new("1.21.6", [], 21, 80, 0, true, true, true, false, false, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast"], TrialChamberLootTables),
        new("1.21.7", [], 21, 81, 0, true, true, true, false, false, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast"], TrialChamberLootTables),
        new("1.21.8", [], 21, 81, 0, true, true, true, false, false, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast"], TrialChamberLootTables),
        new("1.21.9", [], 21, 88, 0, true, true, true, true, false, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem"], TrialChamberLootTables),
        new("1.21.10", [], 21, 88, 0, true, true, true, true, false, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem"], TrialChamberLootTables),
        new("1.21.11", [], 21, 94, 1, true, true, true, true, true, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem", "nautilus", "zombie_nautilus", "camel_husk", "parched"], TrialChamberLootTables),
        new("26.1", ["26.1.0"], 25, 101, 1, true, true, true, true, true, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem", "nautilus", "zombie_nautilus", "camel_husk", "parched"], TrialChamberLootTables),
        new("26.1.1", [], 25, 101, 1, true, true, true, true, true, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem", "nautilus", "zombie_nautilus", "camel_husk", "parched"], TrialChamberLootTables),
        new("26.1.2", [], 25, 101, 1, true, true, true, true, true, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem", "nautilus", "zombie_nautilus", "camel_husk", "parched"], TrialChamberLootTables),
        new("26.2", ["26.2.0"], 25, 107, 1, true, true, true, true, true, ["armadillo", "bogged", "breeze", "creaking", "ghastling", "happy_ghast", "copper_golem", "nautilus", "zombie_nautilus", "camel_husk", "parched", "sulfur_cube"], TrialChamberLootTables)
    ];

    private static readonly Dictionary<string, MinecraftVersionInfo> VersionMap = BuildVersionMap();

    public static IReadOnlyList<MinecraftVersionInfo> SupportedVersions { get; } = Versions;

    public static bool TryGetVersion(string? versionID, out MinecraftVersionInfo version)
    {
        if (string.IsNullOrWhiteSpace(versionID))
        {
            version = null!;
            return false;
        }

        return VersionMap.TryGetValue(versionID.Trim(), out version!);
    }

    public static IReadOnlyList<string> GetAdditionalMobIDs(string? versionID)
    {
        return TryGetVersion(versionID, out MinecraftVersionInfo version)
            ? version.AdditionalMobIDs
            : [];
    }

    public static IReadOnlyList<string> GetAdditionalLootTableIDs(string? versionID)
    {
        return TryGetVersion(versionID, out MinecraftVersionInfo version)
            ? version.AdditionalLootTableIDs
            : [];
    }

    public static bool SupportsPauseWhenEmptySeconds(string? versionID)
    {
        return TryGetVersion(versionID, out MinecraftVersionInfo version)
            && version.DataPackFormatMajor >= 57;
    }

    public static bool SupportsLegacySpawnProperties(string? versionID)
    {
        return !TryGetVersion(versionID, out MinecraftVersionInfo version)
            || version.DataPackFormatMajor < 57;
    }

    public static bool SupportsStatusEffect(string? versionID, string? effectID)
    {
        if (string.IsNullOrWhiteSpace(effectID))
            return false;

        if (!TryGetVersion(versionID, out MinecraftVersionInfo version))
            return true;

        return effectID.Trim() switch
        {
            "infested" or "trial_omen" => version.DataPackFormatMajor >= 48,
            _ => true
        };
    }

    private static Dictionary<string, MinecraftVersionInfo> BuildVersionMap()
    {
        Dictionary<string, MinecraftVersionInfo> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (MinecraftVersionInfo version in Versions)
        {
            map[version.ID] = version;
            foreach (string alias in version.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    map[alias.Trim()] = version;
            }
        }

        return map;
    }

    internal sealed class MinecraftVersionInfo(
        string ID,
        IReadOnlyList<string> aliases,
        int requiredJDK,
        int dataPackFormatMajor,
        int dataPackFormatMinor,
        bool usesSingularFunctionDirectories,
        bool usesItemComponents,
        bool usesInlineTextComponents,
        bool usesModernPackMetadata,
        bool usesNamespacedGameRules,
        IReadOnlyList<string> additionalMobIDs,
        IReadOnlyList<string> additionalLootTableIDs)
    {
        public string ID { get; } = ID;
        public IReadOnlyList<string> Aliases { get; } = aliases ?? [];
        public string DisplayID => Aliases.Count > 0 ? Aliases[0] : ID;
        public int RequiredJDK { get; } = requiredJDK;
        public int DataPackFormatMajor { get; } = dataPackFormatMajor;
        public int DataPackFormatMinor { get; } = dataPackFormatMinor;
        public bool UsesSingularFunctionDirectories { get; } = usesSingularFunctionDirectories;
        public bool UsesItemComponents { get; } = usesItemComponents;
        public bool UsesInlineTextComponents { get; } = usesInlineTextComponents;
        public bool UsesModernPackMetadata { get; } = usesModernPackMetadata;
        public bool UsesNamespacedGameRules { get; } = usesNamespacedGameRules;
        public IReadOnlyList<string> AdditionalMobIDs { get; } = additionalMobIDs ?? [];
        public IReadOnlyList<string> AdditionalLootTableIDs { get; } = additionalLootTableIDs ?? [];

        public string GetExactPackFormatJsonValue()
        {
            if (DataPackFormatMinor > 0 || UsesModernPackMetadata)
                return "[" + DataPackFormatMajor.ToString(CultureInfo.InvariantCulture) + ", " + DataPackFormatMinor.ToString(CultureInfo.InvariantCulture) + "]";

            return DataPackFormatMajor.ToString(CultureInfo.InvariantCulture);
        }
    }
}
