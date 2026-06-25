using System.Collections.Generic;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

internal static class TwitchCraftCatalogs
{
    private static readonly EffectDefinition[] BaseEffects =
    [
        new() { ID = "absorption", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "bad_omen", MinSeconds = 180, MaxSeconds = 300, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "blindness", MinSeconds = 15, MaxSeconds = 20, MinAmplifier = 0, MaxAmplifier = 0 },
        new() { ID = "conduit_power", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "darkness", MinSeconds = 15, MaxSeconds = 20, MinAmplifier = 0, MaxAmplifier = 0 },
        new() { ID = "dolphins_grace", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "fire_resistance", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "glowing", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 0 },
        new() { ID = "haste", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "health_boost", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "hero_of_the_village", MinSeconds = 180, MaxSeconds = 300, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "hunger", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "infested", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 0 },
        new() { ID = "instant_damage", MinSeconds = 1, MaxSeconds = 1, MinAmplifier = 0, MaxAmplifier = 1 },
        new() { ID = "instant_health", MinSeconds = 1, MaxSeconds = 1, MinAmplifier = 0, MaxAmplifier = 1 },
        new() { ID = "invisibility", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 0 },
        new() { ID = "jump_boost", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "levitation", MinSeconds = 15, MaxSeconds = 20, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "luck", MinSeconds = 120, MaxSeconds = 240, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "mining_fatigue", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "nausea", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "night_vision", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "poison", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "regeneration", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "resistance", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "saturation", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "slow_falling", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "slowness", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "speed", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "strength", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "trial_omen", MinSeconds = 30, MaxSeconds = 30, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "unluck", MinSeconds = 120, MaxSeconds = 250, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "water_breathing", MinSeconds = 120, MaxSeconds = 250, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "weakness", MinSeconds = 20, MaxSeconds = 80, MinAmplifier = 0, MaxAmplifier = 4 },
        new() { ID = "wither", MinSeconds = 5, MaxSeconds = 15, MinAmplifier = 0, MaxAmplifier = 2 }
    ];

    private static readonly string[] BaseLootTables =
    [
        "chests/abandoned_mineshaft",
        "chests/ancient_city",
        "chests/ancient_city_ice_box",
        "chests/bastion_bridge",
        "chests/bastion_hoglin_stable",
        "chests/bastion_other",
        "chests/bastion_treasure",
        "chests/buried_treasure",
        "chests/desert_pyramid",
        "chests/end_city_treasure",
        "chests/igloo_chest",
        "chests/jungle_temple",
        "chests/jungle_temple_dispenser",
        "chests/nether_bridge",
        "chests/pillager_outpost",
        "chests/ruined_portal",
        "chests/shipwreck_map",
        "chests/shipwreck_supply",
        "chests/shipwreck_treasure",
        "chests/simple_dungeon",
        "chests/spawn_bonus_chest",
        "chests/stronghold_corridor",
        "chests/stronghold_crossing",
        "chests/stronghold_library",
        "chests/underwater_ruin_big",
        "chests/underwater_ruin_small",
        "chests/woodland_mansion",
        "chests/village/village_armorer",
        "chests/village/village_butcher",
        "chests/village/village_cartographer",
        "chests/village/village_desert_house",
        "chests/village/village_fisher",
        "chests/village/village_fletcher",
        "chests/village/village_mason",
        "chests/village/village_plains_house",
        "chests/village/village_savanna_house",
        "chests/village/village_shepherd",
        "chests/village/village_snowy_house",
        "chests/village/village_taiga_house",
        "chests/village/village_tannery",
        "chests/village/village_temple",
        "chests/village/village_toolsmith",
        "chests/village/village_weaponsmith",
    ];

    private static readonly string[] BaseMobs =
    [
        "allay", "axolotl", "bat", "bee", "blaze", "camel", "cat", "cave_spider", "chicken", "cod", "cow", "creeper",
        "dolphin", "donkey", "drowned", "elder_guardian", "ender_dragon", "enderman", "endermite", "evoker", "fox", "frog",
        "ghast", "giant", "glow_squid", "goat", "guardian", "hoglin", "horse", "husk", "illusioner", "iron_golem", "llama",
        "magma_cube", "mooshroom", "mule", "ocelot", "panda", "parrot", "phantom", "pig", "piglin", "piglin_brute",
        "pillager", "polar_bear", "pufferfish", "rabbit", "ravager", "salmon", "sheep", "shulker", "silverfish", "skeleton",
        "skeleton_horse", "slime", "sniffer", "snow_golem", "spider", "squid", "stray", "strider", "tadpole", "trader_llama",
        "tropical_fish", "turtle", "vex", "villager", "vindicator", "wandering_trader", "warden", "witch", "wither",
        "wither_skeleton", "wolf", "zoglin", "zombie", "zombie_horse", "zombie_villager", "zombified_piglin"
    ];

    public static List<EffectDefinition> BuildEffectList()
    {
        List<EffectDefinition> effects = new(BaseEffects.Length);
        foreach (EffectDefinition effect in BaseEffects)
        {
            effects.Add(new EffectDefinition
            {
                ID = effect.ID,
                MinSeconds = effect.MinSeconds,
                MaxSeconds = effect.MaxSeconds,
                MinAmplifier = effect.MinAmplifier,
                MaxAmplifier = effect.MaxAmplifier
            });
        }

        return effects;
    }

    public static List<string> BuildLootList(string? minecraftVersion = null)
        => [.. BaseLootTables, .. MinecraftVersionSupport.GetAdditionalLootTableIDs(minecraftVersion)];

    public static List<string> BuildMobList(string? minecraftVersion = null)
        => [.. BaseMobs, .. MinecraftVersionSupport.GetAdditionalMobIDs(minecraftVersion)];
}
