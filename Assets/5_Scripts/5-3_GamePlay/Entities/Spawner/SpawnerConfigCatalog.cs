using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SpawnerConfigCatalog
{
    public int SchemaVersion;
    public List<SpawnerConfigDefinition> Configs = new();
}

[Serializable]
public sealed class SpawnerConfigDefinition
{
    public string Id;
    public string ScheduleMode = "timedWindows";
    public string EcologyGroup = "animals";
    public bool RequireGlobalDarkness;
    public float SpawnTriggerTime = 720f;
    public float SpawnTimeTolerance = 1f;
    public int SpawnsPerDay = 1;
    public float MinSpawnDistance = 15f;
    public float MaxSpawnDistance = 50f;
    public float PlayerVisibilityExclusionDistance = 18f;
    public float PlayerPopulationRadius = 60f;
    public int PerPlayerAliveLimit;
    public int SpawnSearchRetryCount = 5;
    public int SpawnCount = 1;
    public float SpawnChance = 1f;
    public int DaysBetweenSpawns = 1;
    public int GroupAliveLimit = 12;
    public int MaxEcologyBudget = 12;
    public int DailyBudgetRecovery = 4;
    public int RecoveryTargetPopulation;
    public float RecoveryCheckInterval = 8f;
    public int GrowthIntervalDays = 3;
    public int MaxLifetimeSpawnCount = 64;
    public float AsyncSpawnInterval;
    public bool UnboundedDailyGrowth;
    public bool IgnorePopulationLimits;
    public bool RequireCompletelyDarkTile = true;
    public float MaxAllowedTileLight = 1f;
    public List<string> AllowedBiomeNames = new();
    public float RecycleDistance = 110f;
    public float RecycleGraceSeconds = 20f;
    public List<SpawnerSpawnEntryDefinition> SpawnEntries = new();

    public SpawnerConfig CreateRuntimeConfig()
    {
        SpawnerConfig config = ScriptableObject.CreateInstance<SpawnerConfig>();
        config.name = Id;
        config.PersistentId = Id;
        config.ScheduleMode = ParseScheduleMode(ScheduleMode);
        config.EcologyGroup = ParseEcologyGroup(EcologyGroup);
        config.RequireGlobalDarkness = RequireGlobalDarkness;
        config.SpawnTriggerTime = SpawnTriggerTime;
        config.SpawnTimeTolerance = SpawnTimeTolerance;
        config.SpawnsPerDay = SpawnsPerDay;
        config.MinSpawnDistance = MinSpawnDistance;
        config.MaxSpawnDistance = MaxSpawnDistance;
        config.PlayerVisibilityExclusionDistance = PlayerVisibilityExclusionDistance;
        config.PlayerPopulationRadius = PlayerPopulationRadius;
        config.PerPlayerAliveLimit = PerPlayerAliveLimit;
        config.SpawnSearchRetryCount = SpawnSearchRetryCount;
        config.SpawnCount = SpawnCount;
        config.SpawnChance = SpawnChance;
        config.DaysBetweenSpawns = DaysBetweenSpawns;
        config.GroupAliveLimit = GroupAliveLimit;
        config.MaxEcologyBudget = MaxEcologyBudget;
        config.DailyBudgetRecovery = DailyBudgetRecovery;
        config.RecoveryTargetPopulation = RecoveryTargetPopulation;
        config.RecoveryCheckInterval = RecoveryCheckInterval;
        config.GrowthIntervalDays = GrowthIntervalDays;
        config.MaxLifetimeSpawnCount = MaxLifetimeSpawnCount;
        config.AsyncSpawnInterval = AsyncSpawnInterval;
        config.UnboundedDailyGrowth = UnboundedDailyGrowth;
        config.IgnorePopulationLimits = IgnorePopulationLimits;
        config.RequireCompletelyDarkTile = RequireCompletelyDarkTile;
        config.MaxAllowedTileLight = MaxAllowedTileLight;
        config.AllowedBiomeNames = AllowedBiomeNames != null
            ? new List<string>(AllowedBiomeNames)
            : new List<string>();
        config.RecycleDistance = RecycleDistance;
        config.RecycleGraceSeconds = RecycleGraceSeconds;
        config.SpawnEntries = new List<SpawnerConfig.SpawnEntry>();

        if (SpawnEntries != null)
        {
            for (int index = 0; index < SpawnEntries.Count; index++)
            {
                SpawnerSpawnEntryDefinition source = SpawnEntries[index];
                if (source == null)
                    continue;

                config.SpawnEntries.Add(source.CreateRuntimeEntry());
            }
        }

        return config;
    }

    private static SpawnerScheduleMode ParseScheduleMode(string value)
    {
        if (string.Equals(value, "dayMilestoneGrowth", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, nameof(SpawnerScheduleMode.DayMilestoneGrowth), StringComparison.OrdinalIgnoreCase))
        {
            return SpawnerScheduleMode.DayMilestoneGrowth;
        }

        return SpawnerScheduleMode.TimedWindows;
    }

    private static SpawnerEcologyGroup ParseEcologyGroup(string value)
    {
        if (string.Equals(value, "commonEnemies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, nameof(SpawnerEcologyGroup.CommonEnemies), StringComparison.OrdinalIgnoreCase))
        {
            return SpawnerEcologyGroup.CommonEnemies;
        }

        if (string.Equals(value, "nightEnemies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, nameof(SpawnerEcologyGroup.NightEnemies), StringComparison.OrdinalIgnoreCase))
        {
            return SpawnerEcologyGroup.NightEnemies;
        }

        return SpawnerEcologyGroup.Animals;
    }
}

[Serializable]
public sealed class SpawnerSpawnEntryDefinition
{
    public string PrefabName;
    public float Probability = 0.5f;
    public int EcologyCost = 1;
    public int SpeciesAliveLimit;
    public SpawnerConfig.SpawnerSpawnInitialization Initialization = new();

    public SpawnerConfig.SpawnEntry CreateRuntimeEntry()
    {
        SpawnerConfig.SpawnEntry entry = new SpawnerConfig.SpawnEntry
        {
            PrefabName = PrefabName,
            Probability = Probability,
            EcologyCost = EcologyCost,
            SpeciesAliveLimit = SpeciesAliveLimit,
            Initialization = new SpawnerConfig.SpawnerSpawnInitialization
            {
                Nutrition = new SpawnerConfig.SpawnerNutritionInitialization
                {
                    Enabled = Initialization?.Nutrition?.Enabled ?? false,
                    MinFoodRate = Initialization?.Nutrition?.MinFoodRate ?? 1f,
                    MaxFoodRate = Initialization?.Nutrition?.MaxFoodRate ?? 1f
                }
            }
        };
        return entry;
    }
}

public static class SpawnerConfigCatalogService
{
    public static SpawnerConfigCatalog Catalog { get; private set; }
    public static bool IsLoaded => Catalog != null;

    public static void ReplaceCatalog(SpawnerConfigCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public static void Reset()
    {
        Catalog = null;
    }

    public static List<SpawnerConfig> CreateRuntimeConfigs()
    {
        var configs = new List<SpawnerConfig>();
        if (Catalog?.Configs == null)
            return configs;

        for (int index = 0; index < Catalog.Configs.Count; index++)
        {
            SpawnerConfigDefinition definition = Catalog.Configs[index];
            if (definition != null)
                configs.Add(definition.CreateRuntimeConfig());
        }

        return configs;
    }
}
