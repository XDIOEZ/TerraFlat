using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

#region 生物生成 JSON 模型

/// <summary>一次加载的本体生成目录；包含全局调度预算和全部独立生成规则，世界运行时只读取内存快照。</summary>
[Serializable]
public sealed class SpawnerConfigCatalog
{
    public int SchemaVersion;
    public SpawnerRuntimeSettings Settings;
    public List<SpawnerConfigDefinition> Configs = new();
}

/// <summary>单个生成规则文件的版本和内容；新物种复制一份 JSON 后修改参数与条目即可接入。</summary>
[Serializable]
public sealed class SpawnerConfigFile
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion;

    [JsonProperty("config", Required = Required.Always)]
    public SpawnerConfigDefinition Config;
}

/// <summary>启动时加载的全局生态预算；所有帧限流和后端容量均由同一 JSON 提供。</summary>
[Serializable]
public sealed class SpawnerRuntimeSettings
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion;

    [JsonProperty("useAiecsBackend", Required = Required.Always)]
    public bool UseAiecsBackend;

    [JsonProperty("globalAliveLimit", Required = Required.Always)]
    public int GlobalAliveLimit;

    [JsonProperty("spawnRetryInterval", Required = Required.Always)]
    public float SpawnRetryInterval;

    [JsonProperty("ecologyTickInterval", Required = Required.Always)]
    public float EcologyTickInterval;

    [JsonProperty("maxLoadedGameObjectActors", Required = Required.Always)]
    public int MaxLoadedGameObjectActors;

    [JsonProperty("maxLoadedEntityActors", Required = Required.Always)]
    public int MaxLoadedEntityActors;

    [JsonProperty("residentChecksPerTick", Required = Required.Always)]
    public int ResidentChecksPerTick;
}

/// <summary>可复用的生成规则参数；数字均由规则 JSON 显式声明并在加载时校验。</summary>
[JsonObject(ItemRequired = Required.Always)]
[Serializable]
public sealed class SpawnerConfigDefinition
{
    public SpawnerTreeHabitat TreeHabitat = new();
    public string Id;
    public string ScheduleMode = "timedWindows";
    public string EcologyGroup = "animals";
    public bool RequireGlobalDarkness;
    public float SpawnTriggerTime = 720f;
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
    [JsonProperty(Required = Required.Default)]
    public List<int> AllowedGroundTileIds = new();
    public float RecycleDistance = 110f;
    public float RecycleGraceSeconds = 20f;
    public List<SpawnerSpawnEntryDefinition> SpawnEntries = new();

    public SpawnerConfig CreateRuntimeConfig()
    {
        SpawnerConfig config = ScriptableObject.CreateInstance<SpawnerConfig>();
        config.name = Id;
        config.PersistentId = Id;
        config.TreeHabitat = new SpawnerTreeHabitat
        {
            Enabled = TreeHabitat.Enabled,
            TreesPerActor = TreeHabitat.TreesPerActor,
            SpawnRadius = TreeHabitat.SpawnRadius
        };
        config.ScheduleMode = ParseScheduleMode(ScheduleMode);
        config.EcologyGroup = ParseEcologyGroup(EcologyGroup);
        config.RequireGlobalDarkness = RequireGlobalDarkness;
        config.SpawnTriggerTime = SpawnTriggerTime;
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
        config.AllowedBiomeNames = new List<string>(AllowedBiomeNames);
        config.AllowedGroundTileIds = new List<int>(AllowedGroundTileIds);
        config.RecycleDistance = RecycleDistance;
        config.RecycleGraceSeconds = RecycleGraceSeconds;
        config.SpawnEntries = new List<SpawnerConfig.SpawnEntry>();

        for (int index = 0; index < SpawnEntries.Count; index++)
        {
            SpawnerSpawnEntryDefinition source = SpawnEntries[index];
            config.SpawnEntries.Add(source.CreateRuntimeEntry());
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
    [JsonProperty(Required = Required.Always)]
    public string PrefabName;
    [JsonProperty(Required = Required.Always)]
    public string RuntimeBackend = "gameObject";
    [JsonProperty(Required = Required.Always)]
    public float Probability = 0.5f;
    [JsonProperty(Required = Required.Always)]
    public int EcologyCost = 1;
    [JsonProperty(Required = Required.Always)]
    public int SpeciesAliveLimit;
    public SpawnerConfig.SpawnerSpawnInitialization Initialization = new();

    public SpawnerConfig.SpawnEntry CreateRuntimeEntry()
    {
        SpawnerConfig.SpawnEntry entry = new SpawnerConfig.SpawnEntry
        {
            PrefabName = PrefabName,
            RuntimeBackend = ParseRuntimeBackend(RuntimeBackend),
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

    private static AiRuntimeBackendKind ParseRuntimeBackend(string value)
    {
        return string.Equals(value, "entities", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(AiRuntimeBackendKind.Entities), StringComparison.OrdinalIgnoreCase)
            ? AiRuntimeBackendKind.Entities
            : AiRuntimeBackendKind.GameObject;
    }
}

#endregion

public static class SpawnerConfigCatalogService
{
    #region 目录生命周期

    /// <summary>隔离候选刷怪目录；正在运行的刷怪进度不属于资源上下文。</summary>
    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => Catalog, value => Catalog = value, (SpawnerConfigCatalog)null);

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
        if (Catalog == null)
            throw new InvalidOperationException("生物生成 JSON 目录尚未加载");

        var configs = new List<SpawnerConfig>(Catalog.Configs.Count);

        for (int index = 0; index < Catalog.Configs.Count; index++)
        {
            SpawnerConfigDefinition definition = Catalog.Configs[index];
            configs.Add(definition.CreateRuntimeConfig());
        }

        return configs;
    }

    #endregion
}

/// <summary>启动时校验每个生成条目的 Actor 引用；新 JSON 写错 ID 时阻止发布资源目录。</summary>
internal sealed class SpawnerResourceCatalogValidator : IResourceCatalogValidator
{
    public string Id => "spawners";

    public void Validate(GameRes resources, List<string> errors)
    {
        SpawnerConfigCatalog catalog = SpawnerConfigCatalogService.Catalog;
        if (catalog == null)
        {
            errors.Add("生物生成 JSON 目录尚未加载");
            return;
        }

        foreach (SpawnerConfigDefinition config in catalog.Configs)
        {
            foreach (SpawnerSpawnEntryDefinition entry in config.SpawnEntries)
            {
                if (!resources.TryGetActorDefinition(entry.PrefabName, out _))
                    errors.Add($"生成规则 {config.Id} -> Actor {entry.PrefabName} 未注册");
            }
        }
    }
}
