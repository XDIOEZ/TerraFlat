using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>玩家与生物共用的水流推动配置；未列出的实体按权威单体重量计算速度。</summary>
[Serializable]
public sealed class WaterCurrentPushConfigDto
{
    #region JSON 字段

    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
    [JsonProperty("weightRule", Required = Required.Always)] public WaterCurrentWeightRuleDto WeightRule;
    [JsonProperty("actors", Required = Required.Always)] public List<WaterCurrentActorPushDto> Actors;

    #endregion
}

/// <summary>未单独列出的实体按最大流速和半速体重换算，单位为格/秒与千克。</summary>
[Serializable]
public sealed class WaterCurrentWeightRuleDto
{
    #region JSON 字段

    [JsonProperty("maxPushSpeed", Required = Required.Always)] public float MaxPushSpeed;
    [JsonProperty("halfSpeedWeightKg", Required = Required.Always)] public float HalfSpeedWeightKg;

    #endregion
}

/// <summary>单个稳定 Actor ID 的水流推动速度；零表示该生物不会被水流推动。</summary>
[Serializable]
public sealed class WaterCurrentActorPushDto
{
    #region JSON 字段

    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("pushSpeed", Required = Required.Always)] public float PushSpeed;

    #endregion
}

/// <summary>资源会话内的只读水流推动目录，避免移动 Tick 重读或解析 JSON。</summary>
public sealed class WaterCurrentPushCatalog
{
    #region 目录状态

    private readonly Dictionary<string, float> actorSpeeds;
    public float MaxPushSpeed { get; }
    public float HalfSpeedWeightKg { get; }

    internal WaterCurrentPushCatalog(float maxPushSpeed, float halfSpeedWeightKg,
        Dictionary<string, float> actorSpeeds)
    {
        MaxPushSpeed = maxPushSpeed;
        HalfSpeedWeightKg = halfSpeedWeightKg;
        this.actorSpeeds = actorSpeeds;
    }

    /// <summary>显式物种速度优先；其余按 maxPushSpeed / (1 + 体重 / halfSpeedWeightKg) 单调减速。</summary>
    public float ResolvePushSpeed(string actorId, float weightKg)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new InvalidOperationException("水流推动无法解析空 Actor ID。");
        if (actorSpeeds.TryGetValue(actorId.Trim(), out float speed)) return speed;
        if (float.IsNaN(weightKg) || float.IsInfinity(weightKg) || weightKg < 0f)
            throw new InvalidOperationException($"实体 {actorId} 的重量必须是非负有限数：{weightKg}");
        return MaxPushSpeed / (1f + weightKg / HalfSpeedWeightKg);
    }

    #endregion
}

/// <summary>严格读取本体水流推动 JSON，并在资源阶段拒绝无效速度与重复物种 ID。</summary>
public static class WaterCurrentPushConfigLoader
{
    #region 路径与解析

    public const int SupportedSchemaVersion = 2;
    public const string RelativeConfigPath = "GameConfig/Movement/water-current.json";
    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    /// <summary>异步读取资源目录中的唯一水流推动配置。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<WaterCurrentPushCatalog> completed, Action<Exception> failed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInConfigPath, text => json = text, exception => readError = exception);
        if (readError != null)
        {
            failed?.Invoke(readError);
            yield break;
        }

        WaterCurrentPushCatalog catalog;
        try { catalog = Deserialize(json); }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }
        completed?.Invoke(catalog);
    }

    /// <summary>将 JSON 校验并冻结为可按稳定 Actor ID 查找的目录。</summary>
    public static WaterCurrentPushCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("水流推动 JSON 为空。");
        WaterCurrentPushConfigDto dto = JsonConvert.DeserializeObject<WaterCurrentPushConfigDto>(
            json, StrictJsonSettings);
        if (dto == null || dto.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的水流推动 schemaVersion：{dto?.SchemaVersion}");
        if (dto.WeightRule == null)
            throw new InvalidDataException("水流推动 JSON 缺少 weightRule。");
        ValidateSpeed(dto.WeightRule.MaxPushSpeed, "weightRule.maxPushSpeed");
        if (float.IsNaN(dto.WeightRule.HalfSpeedWeightKg) ||
            float.IsInfinity(dto.WeightRule.HalfSpeedWeightKg) || dto.WeightRule.HalfSpeedWeightKg <= 0f)
            throw new InvalidDataException("水流推动 weightRule.halfSpeedWeightKg 必须是正有限数。");
        if (dto.Actors == null)
            throw new InvalidDataException("水流推动 JSON 缺少 actors 数组。");

        var speeds = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (WaterCurrentActorPushDto actor in dto.Actors)
        {
            string id = actor?.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("水流推动 actors 包含空 Actor ID。");
            ValidateSpeed(actor.PushSpeed, $"actors[{id}].pushSpeed");
            if (!speeds.TryAdd(id, actor.PushSpeed))
                throw new InvalidDataException($"水流推动 actors 包含重复 ID：{id}");
        }
        return new WaterCurrentPushCatalog(dto.WeightRule.MaxPushSpeed,
            dto.WeightRule.HalfSpeedWeightKg, speeds);
    }

    private static void ValidateSpeed(float speed, string field)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f)
            throw new InvalidDataException($"水流推动 {field} 必须是非负有限数：{speed}");
    }

    #endregion
}

/// <summary>玩家和所有生物的水流速度统一从当前资源会话读取，F5 发布后立即生效。</summary>
public static class WaterCurrentPushConfigService
{
    #region 资源会话

    private static WaterCurrentPushCatalog catalog;
    public static uint Revision { get; private set; } // 已发布配置版本，供 ECS 居民按需同步。

    public static void ReplaceCatalog(WaterCurrentPushCatalog value) =>
        SetCatalog(value ?? throw new ArgumentNullException(nameof(value)));

    internal static void ConfigureResourceReload(ResourceReloadContext context) =>
        context.Add(() => catalog, SetCatalog, (WaterCurrentPushCatalog)null);

    internal static void Reset() => catalog = null;

    /// <summary>目录发布或回滚时更新版本；清空中的资源阶段不触发生物同步。</summary>
    private static void SetCatalog(WaterCurrentPushCatalog value)
    {
        catalog = value;
        if (value != null) Revision++;
    }

    /// <summary>玩家用稳定身份 Player，其他生物用 Actor 定义 ID 查询统一目录。</summary>
    public static float ResolvePushSpeed(Item item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.itemData?.Stack == null)
            throw new InvalidOperationException("水流推动实体缺少重量数据：" + item.name);
        string actorId = item is Player ? "Player" : item.itemData?.IDName;
        return ResolvePushSpeed(actorId, item.itemData.Stack.Weight);
    }

    /// <summary>纯 ECS 生物在模板编译阶段按 Actor ID 与定义重量读取相同目录。</summary>
    public static float ResolvePushSpeed(string actorId, float weightKg)
    {
        if (catalog == null)
            throw new InvalidOperationException("水流推动配置尚未加载。");
        return catalog.ResolvePushSpeed(actorId, weightKg);
    }

    /// <summary>资源重载后从当前 Actor 定义重新读取重量，供现有 ECS 居民刷新。</summary>
    public static float ResolvePushSpeed(string actorId)
    {
        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(actorId, out RuntimeItemDefinition source) ||
            !source.IsActor)
            throw new InvalidOperationException("水流推动无法读取 Actor 定义：" + actorId);
        ItemData data = source.CreateItemData();
        if (data?.Stack == null)
            throw new InvalidOperationException("水流推动 Actor 定义缺少重量数据：" + actorId);
        return ResolvePushSpeed(actorId, data.Stack.Weight);
    }

    #endregion
}
