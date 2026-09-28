using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>只在资源加载阶段读取 Resources JSON；独立规则文件自动发现，游玩帧内只使用已校验目录。</summary>
public static class SpawnerConfigCatalogLoader
{
    #region 文件加载

    public const int SupportedSchemaVersion = 1;
    public const string SettingsResourcePath = "GameConfig/Spawners/spawner-settings";
    public const string RuleResourcePath = "GameConfig/Spawners/Rules";
    public const int MaximumConfigCharacters = 256 * 1024;

    private static readonly JsonSerializerSettings StrictJsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static SpawnerConfigCatalog LoadBuiltIn()
    {
        TextAsset settingsAsset = Resources.Load<TextAsset>(SettingsResourcePath);
        if (settingsAsset == null)
            throw new FileNotFoundException($"找不到生物生成全局设置：Resources/{SettingsResourcePath}.json");

        TextAsset[] ruleAssets = Resources.LoadAll<TextAsset>(RuleResourcePath);
        if (ruleAssets.Length == 0)
            throw new InvalidDataException($"生物生成规则目录为空：Resources/{RuleResourcePath}");

        Array.Sort(ruleAssets, CompareRuleAssets);
        var catalog = new SpawnerConfigCatalog
        {
            SchemaVersion = SupportedSchemaVersion,
            Settings = DeserializeSettings(settingsAsset.text)
        };
        foreach (TextAsset ruleAsset in ruleAssets)
            catalog.Configs.Add(DeserializeRule(ruleAsset.text, ruleAsset.name));

        Validate(catalog);
        return catalog;
    }

    public static IEnumerator LoadBuiltInAsync(
        Action<SpawnerConfigCatalog> onCompleted,
        Action<Exception> onFailed)
    {
        try
        {
            onCompleted?.Invoke(LoadBuiltIn());
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }

        yield break;
    }

    /// <summary>解析一份独立规则；文件名只用于报错，身份由 JSON 的 id 决定。</summary>
    public static SpawnerConfigDefinition DeserializeRule(string json, string sourceName)
    {
        ValidateText(json, sourceName);
        try
        {
            SpawnerConfigFile file = JsonConvert.DeserializeObject<SpawnerConfigFile>(json, StrictJsonSettings);
            if (file == null || file.SchemaVersion != SupportedSchemaVersion || file.Config == null)
                throw new InvalidDataException($"生物生成规则 {sourceName} 缺少配置或 schemaVersion 不受支持");
            return file.Config;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"生物生成规则 {sourceName} 的 JSON 无效：{exception.Message}", exception);
        }
    }

    /// <summary>解析全局调度和容量设置。</summary>
    public static SpawnerRuntimeSettings DeserializeSettings(string json)
    {
        ValidateText(json, "spawner-settings");
        try
        {
            return JsonConvert.DeserializeObject<SpawnerRuntimeSettings>(json, StrictJsonSettings)
                ?? throw new InvalidDataException("生物生成全局设置为空");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"生物生成全局设置 JSON 无效：{exception.Message}", exception);
        }
    }

    private static void ValidateText(string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"生物生成 JSON 为空：{sourceName}");
        if (json.Length > MaximumConfigCharacters)
            throw new InvalidDataException($"生物生成 JSON 超过字符限制：{sourceName}");
    }

    private static int CompareRuleAssets(TextAsset left, TextAsset right) =>
        StringComparer.Ordinal.Compare(left.name, right.name);

    #endregion

    #region 配置校验

    public static void Validate(SpawnerConfigCatalog catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("生物生成 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的生物生成 schemaVersion：{catalog.SchemaVersion}");
        ValidateSettings(catalog.Settings);
        if (catalog.Configs == null || catalog.Configs.Count == 0)
            throw new InvalidDataException("生物生成配置至少需要一个 config");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < catalog.Configs.Count; index++)
        {
            SpawnerConfigDefinition config = catalog.Configs[index];
            ValidateConfig(config, index, ids);
        }
    }

    private static void ValidateSettings(SpawnerRuntimeSettings settings)
    {
        if (settings == null || settings.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException("生物生成全局设置缺失或 schemaVersion 不受支持");
        if (settings.GlobalAliveLimit < 1 ||
            !IsFinite(settings.SpawnRetryInterval) || settings.SpawnRetryInterval <= 0f ||
            !IsFinite(settings.EcologyTickInterval) || settings.EcologyTickInterval < 0.1f ||
            settings.MaxLoadedGameObjectActors < 1 || settings.MaxLoadedEntityActors < 1 ||
            settings.ResidentChecksPerTick < 1)
        {
            throw new InvalidDataException("生物生成全局调度或装载上限无效");
        }
    }

    private static void ValidateConfig(
        SpawnerConfigDefinition config,
        int index,
        ISet<string> ids)
    {
        if (config == null)
            throw new InvalidDataException($"生物生成 config[{index}] 为空");

        config.Id = config.Id?.Trim();
        if (string.IsNullOrWhiteSpace(config.Id))
            throw new InvalidDataException($"生物生成 config[{index}] 缺少 id");
        if (!ids.Add(config.Id))
            throw new InvalidDataException($"生物生成配置 ID 重复：{config.Id}");

        if (!IsSupportedScheduleMode(config.ScheduleMode))
            throw new InvalidDataException($"生物生成配置 {config.Id} 的 scheduleMode 不受支持：{config.ScheduleMode}");
        if (!IsSupportedEcologyGroup(config.EcologyGroup))
            throw new InvalidDataException($"生物生成配置 {config.Id} 的 ecologyGroup 不受支持：{config.EcologyGroup}");
        if (config.SpawnEntries == null || config.SpawnEntries.Count == 0)
            throw new InvalidDataException($"生物生成配置 {config.Id} 至少需要一个 spawnEntry");
        if (!IsFinite(config.SpawnChance) || config.SpawnChance < 0f || config.SpawnChance > 1f)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的 spawnChance 无效：{config.SpawnChance}");
        if (config.SpawnsPerDay < 1 || config.SpawnCount < 1 || config.SpawnSearchRetryCount < 1)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的数量或重试参数无效");
        if (!IsFinite(config.MinSpawnDistance) || !IsFinite(config.MaxSpawnDistance) ||
            config.MinSpawnDistance < 0f || config.MaxSpawnDistance < config.MinSpawnDistance)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的生成距离无效");
        if (!IsFinite(config.SpawnTriggerTime) || config.SpawnTriggerTime < 0f ||
            !IsFinite(config.PlayerVisibilityExclusionDistance) || config.PlayerVisibilityExclusionDistance < 0f ||
            !IsFinite(config.PlayerPopulationRadius) || config.PlayerPopulationRadius <= 0f ||
            config.PerPlayerAliveLimit < 0 || config.DaysBetweenSpawns < 1 ||
            config.DailyBudgetRecovery < 0 || config.RecoveryTargetPopulation < 0 ||
            !IsFinite(config.RecoveryCheckInterval) || config.RecoveryCheckInterval <= 0f ||
            config.GrowthIntervalDays < 1 || config.MaxLifetimeSpawnCount < 1 ||
            !IsFinite(config.AsyncSpawnInterval) || config.AsyncSpawnInterval < 0f ||
            !IsFinite(config.MaxAllowedTileLight) || config.MaxAllowedTileLight < 0f || config.MaxAllowedTileLight > 1f ||
            !IsFinite(config.RecycleDistance) || config.RecycleDistance < 0f ||
            !IsFinite(config.RecycleGraceSeconds) || config.RecycleGraceSeconds < 0f)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的时间、光照或生态数值无效");
        if (config.GroupAliveLimit < 1 || config.MaxEcologyBudget < 1)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的生态上限无效");
        if (config.AllowedGroundTileIds == null || config.AllowedGroundTileIds.Exists(tileId => tileId <= 0))
            throw new InvalidDataException($"生物生成配置 {config.Id} 的地表 Tile ID 无效");

        HashSet<string> speciesIds = new(StringComparer.OrdinalIgnoreCase);
        if (config.TreeHabitat == null || config.TreeHabitat.TreesPerActor < 1 ||
            !IsFinite(config.TreeHabitat.SpawnRadius) || config.TreeHabitat.SpawnRadius <= 0f)
            throw new InvalidDataException($"生成配置 {config.Id} 的树木栖息地参数无效。");
        for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
        {
            ValidateEntry(config, config.SpawnEntries[entryIndex], entryIndex, speciesIds);
        }
    }

    private static void ValidateEntry(
        SpawnerConfigDefinition config,
        SpawnerSpawnEntryDefinition entry,
        int index,
        ISet<string> speciesIds)
    {
        if (entry == null)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的 spawnEntry[{index}] 为空");

        entry.PrefabName = entry.PrefabName?.Trim();
        if (string.IsNullOrWhiteSpace(entry.PrefabName))
            throw new InvalidDataException($"生物生成配置 {config.Id} 的 spawnEntry[{index}] 缺少 prefabName");
        if (!speciesIds.Add(entry.PrefabName))
            throw new InvalidDataException($"生物生成配置 {config.Id} 重复声明物种：{entry.PrefabName}");
        if (!IsSupportedRuntimeBackend(entry.RuntimeBackend))
            throw new InvalidDataException($"物种 {entry.PrefabName} 的 runtimeBackend 不受支持：{entry.RuntimeBackend}");
        if (!IsFinite(entry.Probability) || entry.Probability <= 0f)
            throw new InvalidDataException($"物种 {entry.PrefabName} 的 probability 无效：{entry.Probability}");
        if (entry.EcologyCost < 1 || entry.SpeciesAliveLimit < 0)
            throw new InvalidDataException($"物种 {entry.PrefabName} 的生态参数无效");

        SpawnerConfig.SpawnerNutritionInitialization nutrition = entry.Initialization?.Nutrition;
        if (nutrition == null)
            return;
        if (!IsFinite(nutrition.MinFoodRate) || !IsFinite(nutrition.MaxFoodRate) ||
            nutrition.MinFoodRate < 0f || nutrition.MinFoodRate > 1f ||
            nutrition.MaxFoodRate < 0f || nutrition.MaxFoodRate > 1f ||
            nutrition.MinFoodRate > nutrition.MaxFoodRate)
        {
            throw new InvalidDataException($"物种 {entry.PrefabName} 的出生饱食度范围无效");
        }
    }

    private static bool IsSupportedScheduleMode(string value)
    {
        return string.Equals(value, "timedWindows", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(SpawnerScheduleMode.TimedWindows), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "dayMilestoneGrowth", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(SpawnerScheduleMode.DayMilestoneGrowth), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedEcologyGroup(string value)
    {
        return string.Equals(value, "animals", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(SpawnerEcologyGroup.Animals), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "commonEnemies", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(SpawnerEcologyGroup.CommonEnemies), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "nightEnemies", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(SpawnerEcologyGroup.NightEnemies), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedRuntimeBackend(string value)
    {
        return string.Equals(value, "entities", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, nameof(AiRuntimeBackendKind.Entities), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    #endregion
}
