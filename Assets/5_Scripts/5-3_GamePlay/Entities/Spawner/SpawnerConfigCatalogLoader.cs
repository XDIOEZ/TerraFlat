using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class SpawnerConfigCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeSpawnerRoot = "GameConfig/Spawners";
    public const string ConfigFileName = "spawner-manifest.json";
    public const string RelativeConfigPath = RelativeSpawnerRoot + "/" + ConfigFileName;
    public const long MaximumConfigBytes = 1024 * 1024;

    private static readonly JsonSerializerSettings StrictJsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    public static SpawnerConfigCatalog LoadBuiltIn()
    {
        return Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInConfigPath));
    }

    public static IEnumerator LoadBuiltInAsync(
        Action<SpawnerConfigCatalog> onCompleted,
        Action<Exception> onFailed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInConfigPath,
            text => json = text,
            exception => readError = exception);

        if (readError != null)
        {
            onFailed?.Invoke(readError);
            yield break;
        }

        try
        {
            onCompleted?.Invoke(Deserialize(json));
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    public static SpawnerConfigCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("生物生成 JSON 为空");
        if (json.Length > MaximumConfigBytes)
            throw new InvalidDataException($"生物生成 JSON 超过大小限制：{json.Length} bytes");

        SpawnerConfigCatalog catalog = JsonConvert.DeserializeObject<SpawnerConfigCatalog>(
            json,
            StrictJsonSettings);
        Validate(catalog);
        return catalog;
    }

    public static void Validate(SpawnerConfigCatalog catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("生物生成 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的生物生成 schemaVersion：{catalog.SchemaVersion}");
        if (catalog.Configs == null || catalog.Configs.Count == 0)
            throw new InvalidDataException("生物生成配置至少需要一个 config");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < catalog.Configs.Count; index++)
        {
            SpawnerConfigDefinition config = catalog.Configs[index];
            ValidateConfig(config, index, ids);
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
        if (config.MinSpawnDistance < 0f || config.MaxSpawnDistance < config.MinSpawnDistance)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的生成距离无效");
        if (config.GroupAliveLimit < 1 || config.MaxEcologyBudget < 1)
            throw new InvalidDataException($"生物生成配置 {config.Id} 的生态上限无效");

        HashSet<string> speciesIds = new(StringComparer.OrdinalIgnoreCase);
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
        if (!IsFinite(entry.Probability) || entry.Probability <= 0f)
            throw new InvalidDataException($"物种 {entry.PrefabName} 的 probability 无效：{entry.Probability}");
        if (entry.EcologyCost < 1 || entry.SpeciesAliveLimit < 0)
            throw new InvalidDataException($"物种 {entry.PrefabName} 的生态参数无效");

        SpawnerConfig.SpawnerNutritionInitialization nutrition = entry.Initialization?.Nutrition;
        if (nutrition == null)
            return;
        if (!IsFinite(nutrition.MinFoodRate) || !IsFinite(nutrition.MaxFoodRate) ||
            nutrition.MinFoodRate < 0f || nutrition.MinFoodRate > 1f ||
            nutrition.MaxFoodRate < 0f || nutrition.MaxFoodRate > 1f)
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

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
