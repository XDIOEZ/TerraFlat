using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>液体加热处理模式；定义只描述数据，具体库存事务由加热模块执行。</summary>
public enum LiquidHeatProcessMode
{
    Transform = 0,
    ConsumeServing = 1
}

/// <summary>一种液体的加热处理规则，可由本体或 MOD JSON 注册。</summary>
public sealed class LiquidHeatProcess
{
    public LiquidHeatProcess(
        LiquidHeatProcessMode mode,
        float minimumTemperature,
        float seconds,
        string resultLiquidId,
        string outputItemId,
        int consumeAmount,
        int outputAmount)
    {
        Mode = mode;
        MinimumTemperature = minimumTemperature;
        Seconds = seconds;
        ResultLiquidId = resultLiquidId;
        OutputItemId = outputItemId;
        ConsumeAmount = consumeAmount;
        OutputAmount = outputAmount;
    }

    public LiquidHeatProcessMode Mode { get; }
    public float MinimumTemperature { get; }
    public float Seconds { get; }
    public string ResultLiquidId { get; }
    public string OutputItemId { get; }
    public int ConsumeAmount { get; }
    public int OutputAmount { get; }
}

/// <summary>
/// 可注册的通用液体定义。容器只保存 LiquidId 与数量，不再把水质写死在容器类型里；
/// 本体和 MOD 都通过同一目录声明液体的显示、饮用与加热语义。
/// </summary>
public sealed class LiquidDefinition
{
    public LiquidDefinition(
        string id,
        string displayName,
        string description,
        string category,
        string visualState,
        bool drinkable,
        float hydrationPerServing,
        IReadOnlyList<LiquidDrinkEffect> drinkEffects,
        LiquidHeatProcess heatProcess,
        float buoyancyThresholdMultiplier = 1f)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        VisualState = visualState;
        Drinkable = drinkable;
        HydrationPerServing = hydrationPerServing;
        DrinkEffects = drinkEffects ?? Array.Empty<LiquidDrinkEffect>();
        HeatProcess = heatProcess;
        BuoyancyThresholdMultiplier = buoyancyThresholdMultiplier;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public string VisualState { get; }
    public bool Drinkable { get; }
    public float HydrationPerServing { get; }
    public IReadOnlyList<LiquidDrinkEffect> DrinkEffects { get; }
    public LiquidHeatProcess HeatProcess { get; }
    /// <summary>世界掉落物浮沉阈值倍率；1 保持基础阈值不变。</summary>
    public float BuoyancyThresholdMultiplier { get; }
}

/// <summary>一次饮用液体时可能附加的 Buff；概率和反馈属于液体定义，而不是容器或水地块。</summary>
public sealed class LiquidDrinkEffect
{
    public LiquidDrinkEffect(string buffId, float chance, string feedback)
    {
        BuffId = buffId;
        Chance = chance;
        Feedback = feedback;
    }

    public string BuffId { get; }
    public float Chance { get; }
    public string Feedback { get; }
}

/// <summary>液体 JSON DTO；MOD definitionFiles 的 liquids 数组与本体目录共用此 schema。</summary>
[Serializable]
public sealed class LiquidDefinitionDto
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("displayName", Required = Required.Always)]
    public string DisplayName;

    [JsonProperty("description")]
    public string Description = string.Empty;

    [JsonProperty("category")]
    public string Category = "generic";

    [JsonProperty("visualState")]
    public string VisualState = "filled";

    [JsonProperty("drinkable")]
    public bool Drinkable;

    [JsonProperty("hydrationPerServing")]
    public float HydrationPerServing;

    [JsonProperty("drinkEffects")]
    public List<LiquidDrinkEffectDto> DrinkEffects = new();

    [JsonProperty("heatProcess")]
    public LiquidHeatProcessDto HeatProcess;

    [JsonProperty("buoyancyThresholdMultiplier")]
    public float BuoyancyThresholdMultiplier = 1f;

    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;
}

/// <summary>液体饮用后果 DTO；当前通用后果为按概率向饮用者添加 Buff。</summary>
[Serializable]
public sealed class LiquidDrinkEffectDto
{
    [JsonProperty("buffId", Required = Required.Always)]
    public string BuffId;

    [JsonProperty("chance")]
    public float Chance = 1f;

    [JsonProperty("feedback")]
    public string Feedback;
}

/// <summary>单条液体加热规则 DTO。</summary>
[Serializable]
public sealed class LiquidHeatProcessDto
{
    [JsonProperty("mode", Required = Required.Always)]
    public string Mode;

    [JsonProperty("minimumTemperature")]
    public float MinimumTemperature = 100f;

    [JsonProperty("seconds", Required = Required.Always)]
    public float Seconds;

    [JsonProperty("resultLiquidId")]
    public string ResultLiquidId;

    [JsonProperty("outputItemId")]
    public string OutputItemId;

    [JsonProperty("consumeAmount")]
    public int ConsumeAmount = 1;

    [JsonProperty("outputAmount")]
    public int OutputAmount = 1;
}

/// <summary>本体液体目录。</summary>
[Serializable]
public sealed class LiquidCatalogDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion;

    [JsonProperty("liquids", Required = Required.Always)]
    public List<LiquidDefinitionDto> Liquids = new();
}

/// <summary>液体定义的严格解析、校验和运行时构建入口。</summary>
public static class LiquidDefinitionFactory
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    /// <summary>严格解析一条液体定义，供 MOD definitionFiles 复用。</summary>
    public static LiquidDefinitionDto DeserializeDefinition(JToken token)
    {
        if (token == null)
            throw new InvalidDataException("液体定义为空");

        JsonSerializer serializer = JsonSerializer.Create(StrictJsonSettings);
        return token.ToObject<LiquidDefinitionDto>(serializer)
            ?? throw new InvalidDataException("液体定义无法反序列化");
    }

    /// <summary>严格解析本体液体目录。</summary>
    public static LiquidCatalogDto DeserializeCatalog(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("液体目录为空");

        return JsonConvert.DeserializeObject<LiquidCatalogDto>(json, StrictJsonSettings)
            ?? throw new InvalidDataException("液体目录无法反序列化");
    }

    /// <summary>校验并构建一条运行时液体定义。</summary>
    public static LiquidDefinition Build(LiquidDefinitionDto dto)
    {
        if (dto == null)
            throw new InvalidDataException("液体定义 DTO 为空");

        string id = NormalizeContentId(dto.Id, "液体 ID");
        string displayName = NormalizeRequired(dto.DisplayName, $"液体 {id} displayName");
        string category = string.IsNullOrWhiteSpace(dto.Category) ? "generic" : dto.Category.Trim().ToLowerInvariant();
        string visualState = string.IsNullOrWhiteSpace(dto.VisualState) ? "filled" : dto.VisualState.Trim();
        ValidateFinite(dto.HydrationPerServing, id, nameof(dto.HydrationPerServing));
        if (dto.HydrationPerServing < 0f)
            throw new InvalidDataException($"液体 {id} hydrationPerServing 不能小于 0");
        if (!dto.Drinkable && dto.HydrationPerServing > 0f)
            throw new InvalidDataException($"液体 {id} 不可饮用时不能配置 hydrationPerServing");
        ValidateFinite(dto.BuoyancyThresholdMultiplier, id, nameof(dto.BuoyancyThresholdMultiplier));
        if (dto.BuoyancyThresholdMultiplier <= 0f)
            throw new InvalidDataException($"液体 {id} buoyancyThresholdMultiplier 必须大于 0");

        List<LiquidDrinkEffect> drinkEffects = BuildDrinkEffects(id, dto.Drinkable, dto.DrinkEffects);

        LiquidHeatProcess heatProcess = dto.HeatProcess == null
            ? null
            : BuildHeatProcess(id, dto.HeatProcess);

        return new LiquidDefinition(
            id,
            displayName,
            dto.Description?.Trim() ?? string.Empty,
            category,
            visualState,
            dto.Drinkable,
            dto.HydrationPerServing,
            drinkEffects,
            heatProcess,
            dto.BuoyancyThresholdMultiplier);
    }

    /// <summary>构建整个本体分包并拒绝重复 ID。</summary>
    public static List<LiquidDefinition> BuildCatalog(LiquidCatalogDto catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("液体目录为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的液体 schemaVersion：{catalog.SchemaVersion}");

        var definitions = new List<LiquidDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LiquidDefinitionDto dto in catalog.Liquids ?? new List<LiquidDefinitionDto>())
        {
            LiquidDefinition definition = Build(dto);
            if (!ids.Add(definition.Id))
                throw new InvalidDataException($"液体目录包含重复 ID：{definition.Id}");
            definitions.Add(definition);
        }
        return definitions;
    }

    /// <summary>在所有定义都已知后校验跨液体与物品引用，避免注册一半才失败。</summary>
    public static void ValidateReferences(
        IEnumerable<LiquidDefinition> definitions,
        Func<string, bool> liquidExists,
        Func<string, bool> itemExists,
        Func<string, bool> buffExists)
    {
        foreach (LiquidDefinition definition in definitions)
        {
            foreach (LiquidDrinkEffect effect in definition.DrinkEffects)
            {
                if (buffExists != null && !buffExists(effect.BuffId))
                    throw new InvalidDataException($"液体 {definition.Id} 的饮用 Buff 不存在：{effect.BuffId}");
            }

            LiquidHeatProcess heat = definition.HeatProcess;
            if (heat == null)
                continue;

            if (!string.IsNullOrWhiteSpace(heat.ResultLiquidId) && !liquidExists(heat.ResultLiquidId))
                throw new InvalidDataException($"液体 {definition.Id} 的加热结果不存在：{heat.ResultLiquidId}");
            if (!string.IsNullOrWhiteSpace(heat.OutputItemId) && !itemExists(heat.OutputItemId))
                throw new InvalidDataException($"液体 {definition.Id} 的加热产物物品不存在：{heat.OutputItemId}");
        }
    }

    private static List<LiquidDrinkEffect> BuildDrinkEffects(
        string liquidId,
        bool drinkable,
        IEnumerable<LiquidDrinkEffectDto> dtos)
    {
        var effects = new List<LiquidDrinkEffect>();
        foreach (LiquidDrinkEffectDto dto in dtos ?? Array.Empty<LiquidDrinkEffectDto>())
        {
            if (!drinkable)
                throw new InvalidDataException($"液体 {liquidId} 不可饮用时不能配置 drinkEffects");
            if (dto == null)
                throw new InvalidDataException($"液体 {liquidId} 的 drinkEffects 包含空项");

            string buffId = NormalizeRequired(dto.BuffId, $"液体 {liquidId} drinkEffects.buffId");
            ValidateFinite(dto.Chance, liquidId, "drinkEffects.chance");
            if (dto.Chance < 0f || dto.Chance > 1f)
                throw new InvalidDataException($"液体 {liquidId} 的 drinkEffects.chance 必须位于 0 到 1 之间");

            effects.Add(new LiquidDrinkEffect(
                buffId,
                dto.Chance,
                string.IsNullOrWhiteSpace(dto.Feedback) ? null : dto.Feedback.Trim()));
        }
        return effects;
    }

    private static LiquidHeatProcess BuildHeatProcess(string liquidId, LiquidHeatProcessDto dto)
    {
        if (!Enum.TryParse(dto.Mode?.Trim(), true, out LiquidHeatProcessMode mode))
            throw new InvalidDataException($"液体 {liquidId} 的 heatProcess.mode 无效：{dto.Mode}");
        ValidateFinite(dto.MinimumTemperature, liquidId, nameof(dto.MinimumTemperature));
        ValidateFinite(dto.Seconds, liquidId, nameof(dto.Seconds));
        if (dto.Seconds <= 0f)
            throw new InvalidDataException($"液体 {liquidId} 的 heatProcess.seconds 必须大于 0");

        string resultLiquidId = NormalizeOptionalContentId(dto.ResultLiquidId, $"液体 {liquidId} resultLiquidId");
        string outputItemId = dto.OutputItemId?.Trim();
        if (mode == LiquidHeatProcessMode.Transform && string.IsNullOrWhiteSpace(resultLiquidId))
            throw new InvalidDataException($"液体 {liquidId} 的 Transform 加热规则必须声明 resultLiquidId");
        if (mode == LiquidHeatProcessMode.ConsumeServing && dto.ConsumeAmount <= 0)
            throw new InvalidDataException($"液体 {liquidId} 的 ConsumeServing.consumeAmount 必须大于 0");
        if (!string.IsNullOrWhiteSpace(outputItemId) && dto.OutputAmount <= 0)
            throw new InvalidDataException($"液体 {liquidId} 的 heatProcess.outputAmount 必须大于 0");

        return new LiquidHeatProcess(
            mode,
            dto.MinimumTemperature,
            dto.Seconds,
            resultLiquidId,
            outputItemId,
            mode == LiquidHeatProcessMode.ConsumeServing ? dto.ConsumeAmount : 0,
            string.IsNullOrWhiteSpace(outputItemId) ? 0 : dto.OutputAmount);
    }

    private static string NormalizeContentId(string value, string field)
    {
        string normalized = NormalizeRequired(value, field);
        if (!normalized.Contains(':', StringComparison.Ordinal) || normalized.StartsWith(":", StringComparison.Ordinal) ||
            normalized.EndsWith(":", StringComparison.Ordinal) || ContainsWhitespaceOrControl(normalized))
        {
            throw new InvalidDataException($"{field} 必须是带稳定命名空间且不含空白的 ID：{normalized}");
        }
        return normalized;
    }

    private static string NormalizeOptionalContentId(string value, string field)
    {
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeContentId(value, field);
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{field} 不能为空");
        return value.Trim();
    }

    private static void ValidateFinite(float value, string id, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"液体 {id} 的 {field} 必须是有限数值");
    }

    private static bool ContainsWhitespaceOrControl(string value)
    {
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                return true;
        }
        return false;
    }
}

/// <summary>本体液体稳定 ID；容器、地形取水和加工逻辑只引用这些 ID。</summary>
public static class LiquidIds
{
    public const string DirtyWater = "core:dirty_water";
    public const string DrinkableWater = "core:drinkable_water";
    public const string SeaWater = "core:sea_water";
}

/// <summary>
/// 统一结算液体饮用后的状态后果。世界水源与液体容器必须走这里，避免同一种液体出现两套规则。
/// </summary>
public static class LiquidDrinkEffectProcessor
{
    public readonly struct Result
    {
        public Result(bool anyEffectTriggered, bool feedbackShown)
        {
            AnyEffectTriggered = anyEffectTriggered;
            FeedbackShown = feedbackShown;
        }

        public bool AnyEffectTriggered { get; }
        public bool FeedbackShown { get; }
    }

    /// <summary>按定义逐条判定饮用后果；传入固定随机值可用于确定性验证。</summary>
    public static Result Apply(Item actor, LiquidDefinition liquid, Func<float> random01 = null, bool showFeedback = true)
    {
        if (actor == null || liquid == null || liquid.DrinkEffects == null || liquid.DrinkEffects.Count == 0)
            return default;

        BuffManager buffManager = actor.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        bool anyEffectTriggered = false;
        bool feedbackShown = false;

        foreach (LiquidDrinkEffect effect in liquid.DrinkEffects)
        {
            bool triggered = effect.Chance >= 1f;
            if (!triggered && effect.Chance > 0f)
            {
                float roll = Mathf.Clamp01(random01 != null ? random01() : UnityEngine.Random.value);
                triggered = roll < effect.Chance;
            }

            if (!triggered)
                continue;

            anyEffectTriggered = true;
            buffManager?.AddBuff(effect.BuffId);
            if (showFeedback && !string.IsNullOrWhiteSpace(effect.Feedback))
            {
                ItemActionFeedback.Show(actor, effect.Feedback);
                feedbackShown = true;
            }
        }

        return new Result(anyEffectTriggered, feedbackShown);
    }
}
