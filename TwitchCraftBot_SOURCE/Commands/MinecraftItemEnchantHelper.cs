using System;
using System.Globalization;

namespace TwitchCraftBot_V1;

internal static class MinecraftItemEnchantHelper
{
    private sealed record EnchantOption(string ID, int MaxLevel);

    private static readonly EnchantOption[] BaseEnchantments =
    [
        new("aqua_affinity", 1),
        new("bane_of_arthropods", 5),
        new("binding_curse", 1),
        new("blast_protection", 4),
        new("channeling", 1),
        new("depth_strider", 3),
        new("efficiency", 5),
        new("feather_falling", 4),
        new("fire_aspect", 2),
        new("fire_protection", 4),
        new("flame", 1),
        new("fortune", 3),
        new("frost_walker", 2),
        new("impaling", 5),
        new("infinity", 1),
        new("knockback", 2),
        new("looting", 3),
        new("loyalty", 3),
        new("luck_of_the_sea", 3),
        new("lure", 3),
        new("mending", 1),
        new("multishot", 1),
        new("piercing", 4),
        new("power", 5),
        new("projectile_protection", 4),
        new("protection", 4),
        new("punch", 2),
        new("quick_charge", 3),
        new("respiration", 3),
        new("riptide", 3),
        new("sharpness", 5),
        new("silk_touch", 1),
        new("smite", 5),
        new("soul_speed", 3),
        new("sweeping_edge", 3),
        new("swift_sneak", 3),
        new("thorns", 3),
        new("unbreaking", 3),
        new("vanishing_curse", 1)
    ];

    private static readonly EnchantOption[] MaceEnchantments =
    [
        new("breach", 4),
        new("density", 5),
        new("wind_burst", 3)
    ];

    internal static void PickEnchant(
        Random random,
        bool supportsMaceEnchantments,
        out string enchantID,
        out string prettyEnchantName,
        out int level)
    {
        ArgumentNullException.ThrowIfNull(random);

        int optionCount = BaseEnchantments.Length + (supportsMaceEnchantments ? MaceEnchantments.Length : 0);
        int selectedIndex = random.Next(optionCount);
        EnchantOption selected = selectedIndex < BaseEnchantments.Length
            ? BaseEnchantments[selectedIndex]
            : MaceEnchantments[selectedIndex - BaseEnchantments.Length];

        enchantID = selected.ID;
        prettyEnchantName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(selected.ID.Replace('_', ' '));
        level = random.Next(1, selected.MaxLevel + 1);
    }

    internal static string BuildEnchant(string selector, string enchantID, int level)
        => "enchant " + selector + " minecraft:" + enchantID + " " + Math.Max(1, level).ToString(CultureInfo.InvariantCulture);
}
