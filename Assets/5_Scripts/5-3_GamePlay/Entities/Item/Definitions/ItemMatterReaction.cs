using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using MemoryPack;

#region 配置数据

[Serializable]
[MemoryPackable]
public sealed partial class ItemMatterDefinitionDto
{
    [JsonProperty("ignitionTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? IgnitionTemperature;

    [JsonProperty("initialMoisture")]
    public float InitialMoisture;

    [JsonProperty("evaporationRatePerSecond")]
    public float EvaporationRatePerSecond;

    [JsonProperty("evaporationStartTemperature")]
    public float EvaporationStartTemperature;

    [JsonProperty("temperatureDryingMultiplierPer10C")]
    public float TemperatureDryingMultiplierPer10C = 0.25f;

    [JsonProperty("waterAbsorptionRatePerSecond")]
    public float WaterAbsorptionRatePerSecond;

    [JsonProperty("transitions")]
    public List<ItemMatterTransitionDto> Transitions = new();
}

[Serializable]
[MemoryPackable]
public sealed partial class ItemMatterTransitionDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("minTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MinTemperature;

    [JsonProperty("maxTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MaxTemperature;

    [JsonProperty("minMoisture", NullValueHandling = NullValueHandling.Ignore)]
    public float? MinMoisture;

    [JsonProperty("maxMoisture", NullValueHandling = NullValueHandling.Ignore)]
    public float? MaxMoisture;

    [JsonProperty("outputItemId", NullValueHandling = NullValueHandling.Ignore)]
    public string OutputItemId;

    [JsonProperty("inputAmount")]
    public int InputAmount = 1;

    [JsonProperty("remainderOutputItemId", NullValueHandling = NullValueHandling.Ignore)]
    public string RemainderOutputItemId;

    [JsonProperty("outputAmountMultiplier")]
    public float OutputAmountMultiplier = 1f;

    [JsonProperty("liquidOutput", NullValueHandling = NullValueHandling.Ignore)]
    public ItemReactionLiquidOutputDto LiquidOutput;
}

[Serializable]
[MemoryPackable]
public sealed partial class ItemReactionDefinitionDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("minTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MinTemperature;

    [JsonProperty("maxTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MaxTemperature;

    [JsonProperty("inputs")]
    public List<ItemReactionIngredientDto> Inputs = new();

    [JsonProperty("outputs")]
    public List<ItemReactionOutputDto> Outputs = new();

    [JsonProperty("liquidOutput", NullValueHandling = NullValueHandling.Ignore)]
    public ItemReactionLiquidOutputDto LiquidOutput;
}

[Serializable]
[MemoryPackable]
public sealed partial class ItemReactionIngredientDto
{
    [JsonProperty("match")]
    public string Match = "exact_item";

    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("tag")]
    public string Tag;

    [JsonProperty("amount")]
    public int Amount = 1;
}

[Serializable]
[MemoryPackable]
public sealed partial class ItemReactionOutputDto
{
    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("amount")]
    public int Amount = 1;

    [JsonProperty("durabilityMultiplier", NullValueHandling = NullValueHandling.Ignore)]
    public float? DurabilityMultiplier;
}

[Serializable]
[MemoryPackable]
public sealed partial class ItemReactionLiquidOutputDto
{
    [JsonProperty("liquidId")]
    public string LiquidId;

    [JsonProperty("amount")]
    public float Amount = 1f;
}

#endregion

#region 运行时定义

public sealed class RuntimeItemMatterDefinition
{
    public float? IgnitionTemperature { get; }
    public float InitialMoisture { get; }
    public float EvaporationRatePerSecond { get; }
    public float EvaporationStartTemperature { get; }
    public float TemperatureDryingMultiplierPer10C { get; }
    public float WaterAbsorptionRatePerSecond { get; }
    public IReadOnlyList<RuntimeItemMatterTransition> Transitions { get; }

    public RuntimeItemMatterDefinition(
        float? ignitionTemperature,
        float initialMoisture,
        float evaporationRatePerSecond,
        float evaporationStartTemperature,
        float temperatureDryingMultiplierPer10C,
        float waterAbsorptionRatePerSecond,
        IReadOnlyList<RuntimeItemMatterTransition> transitions)
    {
        IgnitionTemperature = ignitionTemperature;
        InitialMoisture = initialMoisture;
        EvaporationRatePerSecond = evaporationRatePerSecond;
        EvaporationStartTemperature = evaporationStartTemperature;
        TemperatureDryingMultiplierPer10C = temperatureDryingMultiplierPer10C;
        WaterAbsorptionRatePerSecond = waterAbsorptionRatePerSecond;
        Transitions = transitions ?? Array.Empty<RuntimeItemMatterTransition>();
    }
}

public sealed class RuntimeItemMatterTransition
{
    public string Id { get; }
    public float? MinTemperature { get; }
    public float? MaxTemperature { get; }
    public float? MinMoisture { get; }
    public float? MaxMoisture { get; }
    public string OutputItemId { get; }
    public float OutputAmountMultiplier { get; }
    public int InputAmount { get; }
    public string RemainderOutputItemId { get; }
    public RuntimeLiquidOutput LiquidOutput { get; }

    public RuntimeItemMatterTransition(
        string id,
        float? minTemperature,
        float? maxTemperature,
        float? minMoisture,
        float? maxMoisture,
        string outputItemId,
        float outputAmountMultiplier,
        RuntimeLiquidOutput liquidOutput,
        int inputAmount = 1,
        string remainderOutputItemId = null)
    {
        Id = id;
        MinTemperature = minTemperature;
        MaxTemperature = maxTemperature;
        MinMoisture = minMoisture;
        MaxMoisture = maxMoisture;
        OutputItemId = outputItemId;
        OutputAmountMultiplier = outputAmountMultiplier;
        InputAmount = inputAmount;
        RemainderOutputItemId = remainderOutputItemId;
        LiquidOutput = liquidOutput;
    }

    public bool Matches(ItemMatterState state)
    {
        if (state == null || !state.Initialized || !float.IsFinite(state.TemperatureCelsius) || !float.IsFinite(state.Moisture)) return false;
        if (MinTemperature.HasValue && state.TemperatureCelsius < MinTemperature.Value) return false;
        if (MaxTemperature.HasValue && state.TemperatureCelsius > MaxTemperature.Value) return false;
        if (MinMoisture.HasValue && state.Moisture < MinMoisture.Value) return false;
        if (MaxMoisture.HasValue && state.Moisture > MaxMoisture.Value) return false;
        return true;
    }
}

public sealed class RuntimeItemReactionDefinition
{
    public string SourceItemId { get; }
    public string Id { get; }
    public float? MinTemperature { get; }
    public float? MaxTemperature { get; }
    public RuntimeRecipe Recipe { get; }
    public string Signature { get; }
    public RuntimeLiquidOutput LiquidOutput => Recipe.LiquidOutput;

    public RuntimeItemReactionDefinition(
        string sourceItemId,
        string id,
        float? minTemperature,
        float? maxTemperature,
        RuntimeRecipe recipe,
        string signature)
    {
        SourceItemId = sourceItemId;
        Id = id;
        MinTemperature = minTemperature;
        MaxTemperature = maxTemperature;
        Recipe = recipe;
        Signature = signature;
    }

    public bool TemperatureMatches(float temperature)
    {
        return float.IsFinite(temperature) && (!MinTemperature.HasValue || temperature >= MinTemperature.Value) &&
               (!MaxTemperature.HasValue || temperature <= MaxTemperature.Value);
    }
}

#endregion

#region 编译与去重

public static class ItemMatterReactionCompiler
{
    private const float TinderIgnitionTemperature = 180f;
    private const float DefaultCombustibleIgnitionTemperature = 300f;

    public static RuntimeItemMatterDefinition CompileMatter(
        ItemMatterDefinitionDto source,
        IEnumerable<string> tags,
        string itemId)
    {
        float? ignitionTemperature = ResolveIgnitionTemperature(source?.IgnitionTemperature, tags);
        if (source == null && !ignitionTemperature.HasValue) return null;
        source ??= new ItemMatterDefinitionDto();
        if (ignitionTemperature.HasValue)
        {
            ValidateFinite(ignitionTemperature.Value, itemId, "matter.ignitionTemperature");
            if (ignitionTemperature.Value <= 0f)
                throw new InvalidDataException($"物品 {itemId} 的 matter.ignitionTemperature 必须大于 0℃");
        }
        ValidateFinite01(source.InitialMoisture, itemId, "matter.initialMoisture");
        ValidateFiniteNonNegative(source.EvaporationRatePerSecond, itemId, "matter.evaporationRatePerSecond");
        ValidateFinite(source.EvaporationStartTemperature, itemId, "matter.evaporationStartTemperature");
        ValidateFiniteNonNegative(source.TemperatureDryingMultiplierPer10C, itemId, "matter.temperatureDryingMultiplierPer10C");
        ValidateFiniteNonNegative(source.WaterAbsorptionRatePerSecond, itemId, "matter.waterAbsorptionRatePerSecond");

        var transitions = new List<RuntimeItemMatterTransition>();
        foreach (ItemMatterTransitionDto transition in source.Transitions ?? new())
        {
            if (transition == null) throw new InvalidDataException($"物品 {itemId} 包含空 matter transition");
            ValidateOptionalRange(transition.MinTemperature, transition.MaxTemperature, itemId, "temperature");
            ValidateOptionalRange(transition.MinMoisture, transition.MaxMoisture, itemId, "moisture");
            if (transition.MinMoisture.HasValue) ValidateFinite01(transition.MinMoisture.Value, itemId, "transition.minMoisture");
            if (transition.MaxMoisture.HasValue) ValidateFinite01(transition.MaxMoisture.Value, itemId, "transition.maxMoisture");
            if (!IsFinite(transition.OutputAmountMultiplier) || transition.OutputAmountMultiplier <= 0f)
                throw new InvalidDataException($"物品 {itemId} 的 matter transition 产量倍率无效");
            if (transition.InputAmount < 1)
                throw new InvalidDataException($"物品 {itemId} 的 matter transition inputAmount 必须为正整数");
            string outputId = transition.OutputItemId?.Trim();
            RuntimeLiquidOutput liquid = CompileLiquid(transition.LiquidOutput, itemId, "matter transition");
            if (string.IsNullOrWhiteSpace(outputId) == (liquid == null))
                throw new InvalidDataException($"物品 {itemId} 的 matter transition 必须且只能声明一种固体/液体产物");
            transitions.Add(new RuntimeItemMatterTransition(
                string.IsNullOrWhiteSpace(transition.Id) ? $"matter.{itemId}.{transitions.Count}" : transition.Id.Trim(),
                transition.MinTemperature, transition.MaxTemperature,
                transition.MinMoisture, transition.MaxMoisture,
                outputId, transition.OutputAmountMultiplier, liquid, transition.InputAmount, transition.RemainderOutputItemId?.Trim()));
        }

        // 同一堆木材按整组产炭，剩余不足一组的材料独立变灰。
        if (tags?.Contains("Wood", StringComparer.OrdinalIgnoreCase) == true &&
            !transitions.Any(transition => transition.Id == "wood.charcoal"))
            transitions.Add(new RuntimeItemMatterTransition("wood.charcoal", 500f, null, null, null,
                "Charcoal", 1f, null, inputAmount: 3, remainderOutputItemId: "WoodAsh"));

        return new RuntimeItemMatterDefinition(
            ignitionTemperature,
            source.InitialMoisture,
            source.EvaporationRatePerSecond,
            source.EvaporationStartTemperature,
            source.TemperatureDryingMultiplierPer10C,
            source.WaterAbsorptionRatePerSecond,
            transitions.AsReadOnly());
    }

    /// <summary>显式燃点优先；已有引火物、木材和燃料标签自动获得通用燃点。</summary>
    private static float? ResolveIgnitionTemperature(float? configured, IEnumerable<string> tags)
    {
        if (configured.HasValue) return configured.Value;
        bool combustible = false;
        foreach (string tag in tags ?? Array.Empty<string>())
        {
            if (string.Equals(tag, Tag.CombustionTinder, StringComparison.OrdinalIgnoreCase))
                return TinderIgnitionTemperature;
            if (string.Equals(tag, "Wood", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, Tag.CombustionFuel, StringComparison.OrdinalIgnoreCase))
                combustible = true;
        }
        return combustible ? DefaultCombustibleIgnitionTemperature : null;
    }

    public static IReadOnlyList<RuntimeItemReactionDefinition> CompileReactions(
        IEnumerable<ItemReactionDefinitionDto> source,
        string itemId)
    {
        var result = new List<RuntimeItemReactionDefinition>();
        if (source == null) return result.AsReadOnly();

        foreach (ItemReactionDefinitionDto definition in source)
        {
            if (definition == null) throw new InvalidDataException($"物品 {itemId} 包含空 reaction");
            ValidateOptionalRange(definition.MinTemperature, definition.MaxTemperature, itemId, "reaction temperature");
            if (definition.Inputs == null || definition.Inputs.Count == 0)
                throw new InvalidDataException($"物品 {itemId} 的 reaction 缺少 inputs");
            if ((definition.Outputs == null || definition.Outputs.Count == 0) && definition.LiquidOutput == null)
                throw new InvalidDataException($"物品 {itemId} 的 reaction 缺少产物");

            var ingredients = new List<RuntimeRecipeIngredient>();
            foreach (ItemReactionIngredientDto input in definition.Inputs)
            {
                if (input == null || input.Amount < 1)
                    throw new InvalidDataException($"物品 {itemId} 的 reaction 包含无效输入");
                MatchMode mode = ParseMatchMode(input.Match, itemId);
                string exactId = input.ItemId?.Trim();
                string tag = input.Tag?.Trim();
                if (mode == MatchMode.ExactItem && string.IsNullOrWhiteSpace(exactId) ||
                    mode == MatchMode.ByTag && string.IsNullOrWhiteSpace(tag))
                    throw new InvalidDataException($"物品 {itemId} 的 reaction 输入缺少匹配目标");
                ingredients.Add(new RuntimeRecipeIngredient
                {
                    matchMode = mode,
                    ItemName = exactId ?? string.Empty,
                    Tag = tag ?? string.Empty,
                    amount = input.Amount
                });
            }

            var outputs = new List<RuntimeRecipeResult>();
            foreach (ItemReactionOutputDto output in definition.Outputs ?? new())
            {
                string outputId = output?.ItemId?.Trim();
                float durability = output?.DurabilityMultiplier ?? CraftedDurabilityQuality.DefaultMultiplier;
                if (output == null || string.IsNullOrWhiteSpace(outputId) || output.Amount < 1 ||
                    !IsFinite(durability) || durability <= 0f)
                    throw new InvalidDataException($"物品 {itemId} 的 reaction 包含无效固体产物");
                outputs.Add(new RuntimeRecipeResult
                {
                    ItemName = outputId,
                    amount = output.Amount,
                    durabilityMultiplier = durability
                });
            }

            RuntimeLiquidOutput liquid = CompileLiquid(definition.LiquidOutput, itemId, "reaction");
            string id = string.IsNullOrWhiteSpace(definition.Id)
                ? $"item-reaction.{itemId}.{result.Count}"
                : definition.Id.Trim();
            var recipe = new RuntimeRecipe
            {
                Id = id,
                DisplayName = id,
                Temperature = definition.MinTemperature ?? float.MinValue,
                Temperature_Max = definition.MaxTemperature ?? float.MaxValue,
                ProcessingSeconds = 1f,
                ManualWorkSteps = 1,
                inputs = new RuntimeRecipeInput
                {
                    recipeType = RecipeType.Smelting,
                    inputOrder = RecipeInputRule.无规则合成,
                    GridWidth = Mathf.Max(1, ingredients.Count),
                    GridHeight = 1,
                    RowItems_List = ingredients
                },
                outputs = new RuntimeRecipeOutput { results = outputs },
                LiquidOutput = liquid
            };
            string signature = BuildSignature(definition, ingredients, outputs, liquid);
            result.Add(new RuntimeItemReactionDefinition(
                itemId, id, definition.MinTemperature, definition.MaxTemperature,
                recipe, signature));
        }

        return result.AsReadOnly();
    }

    private static MatchMode ParseMatchMode(string value, string itemId)
    {
        return (value ?? "exact_item").Trim().ToLowerInvariant() switch
        {
            "exact_item" => MatchMode.ExactItem,
            "tag" => MatchMode.ByTag,
            _ => throw new InvalidDataException($"物品 {itemId} 的 reaction.match 只能是 exact_item 或 tag")
        };
    }

    private static RuntimeLiquidOutput CompileLiquid(ItemReactionLiquidOutputDto source, string itemId, string context)
    {
        if (source == null) return null;
        string id = source.LiquidId?.Trim();
        if (string.IsNullOrWhiteSpace(id) || !IsFinite(source.Amount) || source.Amount <= 0f)
            throw new InvalidDataException($"物品 {itemId} 的 {context} 液体产物无效");
        return new RuntimeLiquidOutput { LiquidId = id, Amount = source.Amount };
    }

    private static string BuildSignature(
        ItemReactionDefinitionDto definition,
        IReadOnlyList<RuntimeRecipeIngredient> inputs,
        IReadOnlyList<RuntimeRecipeResult> outputs,
        RuntimeLiquidOutput liquid)
    {
        string inputKey = string.Join("|", inputs
            .Select(input => $"{(int)input.matchMode}:{(input.matchMode == MatchMode.ByTag ? input.Tag : input.ItemName)}:{input.amount}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        string outputKey = string.Join("|", outputs
            .Select(output => $"{output.ItemName}:{output.amount}:{F(output.durabilityMultiplier)}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        string liquidKey = liquid == null ? string.Empty : $"{liquid.LiquidId}:{F(liquid.Amount)}";
        return $"T:{F(definition.MinTemperature)}:{F(definition.MaxTemperature)};I:{inputKey};O:{outputKey};L:{liquidKey}";
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string F(float? value) => value.HasValue ? F(value.Value) : "*";
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void ValidateFinite(float value, string itemId, string field)
    {
        if (!IsFinite(value)) throw new InvalidDataException($"物品 {itemId} 的 {field} 必须为有限数值");
    }

    private static void ValidateFiniteNonNegative(float value, string itemId, string field)
    {
        ValidateFinite(value, itemId, field);
        if (value < 0f) throw new InvalidDataException($"物品 {itemId} 的 {field} 不能为负数");
    }

    private static void ValidateFinite01(float value, string itemId, string field)
    {
        ValidateFinite(value, itemId, field);
        if (value < 0f || value > 1f) throw new InvalidDataException($"物品 {itemId} 的 {field} 必须位于 0~1");
    }

    private static void ValidateOptionalRange(float? min, float? max, string itemId, string field)
    {
        if (min.HasValue) ValidateFinite(min.Value, itemId, field + ".min");
        if (max.HasValue) ValidateFinite(max.Value, itemId, field + ".max");
        if (min.HasValue && max.HasValue && max.Value < min.Value)
            throw new InvalidDataException($"物品 {itemId} 的 {field} 最大值不能小于最小值");
    }
}

#endregion

#region 状态推进与反应解析

public static class ItemMatterRuntime
{
    public static bool EnsureInitialized(ItemData item, float ambientTemperature)
    {
        if (item == null || float.IsNaN(ambientTemperature) || float.IsInfinity(ambientTemperature)) return false;
        ItemMatterState state = item.MatterState ??= new ItemMatterState();
        if (state.Initialized) return false;
        state.Initialized = true;
        state.TemperatureCelsius = ambientTemperature;
        state.Moisture = ResolveMatter(item)?.InitialMoisture ?? 0f;
        return true;
    }

    // 摩擦热量分摊给整堆材料，热源上限不能反过来冷却原本更热的材料。
    public static bool AddHeat(ItemData item, float ambientTemperature, float sourceTemperature, float temperaturePerUnit)
    {
        if (item?.Stack == null || item.Stack.Amount <= 0f || temperaturePerUnit <= 0f ||
            float.IsNaN(item.Stack.Amount) || float.IsInfinity(item.Stack.Amount) ||
            float.IsNaN(ambientTemperature) || float.IsInfinity(ambientTemperature) ||
            float.IsNaN(temperaturePerUnit) || float.IsInfinity(temperaturePerUnit) ||
            float.IsNaN(sourceTemperature) || float.IsInfinity(sourceTemperature)) return false;
        bool initialized = EnsureInitialized(item, ambientTemperature);
        if (item.MatterState?.Initialized != true) return false;
        float before = item.MatterState.TemperatureCelsius;
        if (before < sourceTemperature)
            item.MatterState.TemperatureCelsius = Mathf.Min(sourceTemperature,
                before + temperaturePerUnit / Mathf.Max(1f, item.Stack.Amount));
        return initialized || !Mathf.Approximately(before, item.MatterState.TemperatureCelsius);
    }

    public static bool Advance(
        ItemData item,
        float ambientTemperature,
        float airExposure,
        float seconds,
        bool submerged = false,
        float? transferPerSecondOverride = null,
        float heatingRateMultiplier = 1f)
    {
        if (item == null || seconds <= 0f || !float.IsFinite(seconds) ||
            !float.IsFinite(ambientTemperature) || !float.IsFinite(airExposure) ||
            !float.IsFinite(heatingRateMultiplier) || heatingRateMultiplier < 0f ||
            (transferPerSecondOverride.HasValue &&
             (!float.IsFinite(transferPerSecondOverride.Value) || transferPerSecondOverride.Value < 0f))) return false;
        bool initialized = EnsureInitialized(item, ambientTemperature);
        ItemMatterState state = item.MatterState;
        if (state?.Initialized != true) return false;
        RuntimeItemMatterDefinition matter = ResolveMatter(item);

        float exposure = Mathf.Max(0f, airExposure);
        float beforeTemperature = state.TemperatureCelsius;
        float beforeMoisture = state.Moisture;
        // 温度单独使用供热倍率，干燥与吸水继续按真实经过时间推进。
        float temperatureSeconds = ThermalRuntime.ResolveHeatingSeconds(
            beforeTemperature, ambientTemperature, seconds, heatingRateMultiplier);
        state.TemperatureCelsius = transferPerSecondOverride.HasValue
            ? ThermalRuntime.AdvanceTowards(beforeTemperature, ambientTemperature,
                transferPerSecondOverride.Value, temperatureSeconds, Mathf.Max(0.01f, exposure))
            : ThermalRuntime.AdvanceAmbient(beforeTemperature, ambientTemperature, temperatureSeconds);

        if (matter != null)
        {
            if (submerged && matter.WaterAbsorptionRatePerSecond > 0f)
            {
                state.Moisture = Mathf.MoveTowards(state.Moisture, 1f,
                    matter.WaterAbsorptionRatePerSecond * seconds);
            }
            else if (!submerged && matter.EvaporationRatePerSecond > 0f &&
                     state.TemperatureCelsius >= matter.EvaporationStartTemperature)
            {
                float tens = Mathf.Max(0f,
                    (state.TemperatureCelsius - matter.EvaporationStartTemperature) / 10f);
                float thermal = 1f + tens * matter.TemperatureDryingMultiplierPer10C;
                state.Moisture = Mathf.Max(0f,
                    state.Moisture - matter.EvaporationRatePerSecond * exposure * thermal * seconds);
            }
        }

        return initialized || !Mathf.Approximately(beforeTemperature, state.TemperatureCelsius) ||
               !Mathf.Approximately(beforeMoisture, state.Moisture);
    }

    /// <summary>温度达到燃点后持续燃烧；真实水接触会熄灭，燃尽后由世界掉落系统移除。</summary>
    public static bool AdvanceCombustion(ItemData item, float seconds, bool waterContact, bool combustionSupported, out bool consumedAll)
    {
        consumedAll = false;
        if (item?.Stack == null || item.Stack.Amount <= 0f || seconds <= 0f || !float.IsFinite(seconds))
            return false;

        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        ItemMatterState state = item.MatterState;
        if (matter?.IgnitionTemperature is not float ignitionTemperature || state?.Initialized != true)
            return false;

        bool beforeBurning = state.IsBurning;
        float beforeElapsed = state.CombustionElapsedSeconds;
        float beforeAmount = item.Stack.Amount;

        if (waterContact || !combustionSupported)
        {
            state.IsBurning = false;
            state.CombustionElapsedSeconds = 0f;
            return beforeBurning || !Mathf.Approximately(beforeElapsed, 0f);
        }

        if (!state.IsBurning && state.TemperatureCelsius >= ignitionTemperature)
        {
            state.IsBurning = true;
            state.CombustionElapsedSeconds = 0f;
        }

        if (!state.IsBurning)
            return beforeBurning != state.IsBurning;

        float secondsPerUnit = ResolveCombustionSecondsPerUnit(item);
        state.CombustionElapsedSeconds += seconds;
        int consumedUnits = Mathf.FloorToInt(state.CombustionElapsedSeconds / secondsPerUnit);
        if (consumedUnits > 0)
        {
            float consumed = Mathf.Min(item.Stack.Amount, consumedUnits);
            item.Stack.Amount = Mathf.Max(0f, item.Stack.Amount - consumed);
            state.CombustionElapsedSeconds = item.Stack.Amount > 0f
                ? Mathf.Max(0f, state.CombustionElapsedSeconds - consumedUnits * secondsPerUnit)
                : 0f;
        }

        consumedAll = item.Stack.Amount <= 0f;
        if (consumedAll)
            state.IsBurning = false;
        return beforeBurning != state.IsBurning ||
               !Mathf.Approximately(beforeElapsed, state.CombustionElapsedSeconds) ||
               !Mathf.Approximately(beforeAmount, item.Stack.Amount);
    }

    /// <summary>已有燃料数据直接复用其每件燃烧时长；普通可燃物使用短时默认值。</summary>
    private static float ResolveCombustionSecondsPerUnit(ItemData item)
    {
        if (Mod_Fuel.TryResolveItemData(item, out FuelData fuel) && fuel != null &&
            float.IsFinite(fuel.Fuel.x) && fuel.Fuel.x > 0f)
            return Mathf.Max(1f, fuel.Fuel.x);
        return 12f;
    }

    public static RuntimeItemMatterTransition GetHeatingTransition(ItemData item)
    {
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        if (matter == null) return null;
        RuntimeItemMatterTransition first = null;
        IReadOnlyList<RuntimeItemMatterTransition> transitions = matter.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            RuntimeItemMatterTransition transition = transitions[i];
            if (transition.MinTemperature.HasValue && transition.LiquidOutput == null &&
                (first == null || transition.MinTemperature < first.MinTemperature))
                first = transition;
        }
        return first;
    }

    public static RuntimeItemMatterTransition GetMatchedTransition(ItemData item, bool liquid)
    {
        return SelectMatchedTransition(ResolveMatter(item), item?.MatterState, liquid);
    }

    /// <summary>按配置顺序筛选材料条件，索引遍历避免接口枚举器装箱。</summary>
    internal static RuntimeItemMatterTransition SelectMatchedTransition(
        RuntimeItemMatterDefinition matter, ItemMatterState state, bool liquid)
    {
        if (matter == null || state == null) return null;
        IReadOnlyList<RuntimeItemMatterTransition> transitions = matter.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            RuntimeItemMatterTransition transition = transitions[i];
            if ((transition.LiquidOutput != null) == liquid && transition.Matches(state))
                return transition;
        }
        return null;
    }

    public static float GetMoistureProgress01(ItemData item)
    {
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        if (matter == null || item?.MatterState?.Initialized != true) return 0f;
        IReadOnlyList<RuntimeItemMatterTransition> transitions = matter.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            RuntimeItemMatterTransition transition = transitions[i];
            if (!transition.MaxMoisture.HasValue || transition.LiquidOutput != null) continue;
            float start = matter.InitialMoisture;
            float end = transition.MaxMoisture.Value;
            if (start <= end + 0.0001f) continue;
            return Mathf.Clamp01((start - item.MatterState.Moisture) / (start - end));
        }
        return 0f;
    }

    public static bool CanMoistureTransform(ItemData item)
    {
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        return matter?.Transitions?.Any(transition => transition.MaxMoisture.HasValue && transition.LiquidOutput == null) == true;
    }

    // 只生成产物计划，库存与世界后端各自在全部产物可落地后才扣除原堆。
    public static bool TryCreateSolidTransitionOutputs(ItemData source, out IReadOnlyList<ItemData> outputs)
    {
        outputs = Array.Empty<ItemData>();
        if (source?.Stack == null || !float.IsFinite(source.Stack.Amount) || source.Stack.Amount <= 0f ||
            GetMatchedTransition(source, liquid: false) == null) return false;
        var results = new List<ItemData>();
        if (!AppendTransitionOutputs(source, results, new HashSet<string>(StringComparer.Ordinal), 0)) return false;
        outputs = results;
        return results.Count > 0;
    }

    private static bool AppendTransitionOutputs(ItemData source, List<ItemData> outputs, HashSet<string> path, int depth)
    {
        RuntimeItemMatterTransition transition = GetMatchedTransition(source, liquid: false);
        if (transition == null) { MergeOutput(outputs, source); return true; }
        if (depth >= 16 || !path.Add(source.IDName)) return false;
        float amount = source.Stack.Amount;
        float groups = transition.InputAmount > 1 ? Mathf.Floor(amount / transition.InputAmount) : amount;
        float remainder = transition.InputAmount > 1 ? amount - groups * transition.InputAmount : 0f;
        if (remainder > CraftingIngredientMatcher.AmountEpsilon && string.IsNullOrWhiteSpace(transition.RemainderOutputItemId))
        { path.Remove(source.IDName); return false; }
        bool success = AppendProduct(transition.OutputItemId, groups * transition.OutputAmountMultiplier) &&
                       AppendProduct(transition.RemainderOutputItemId, remainder);
        path.Remove(source.IDName);
        return success;

        bool AppendProduct(string id, float count)
        {
            if (count <= CraftingIngredientMatcher.AmountEpsilon) return true;
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null || !resources.TryGetItemDefinition(id, out RuntimeItemDefinition definition)) return false;
            ItemData product = resources.CreateItemData(id);
            if (product?.Stack == null) return false;
            product.Stack.Amount = count;
            product.Stack.CanBePickedUp = source.Stack.CanBePickedUp;
            product.transform = FastCloner.FastCloner.DeepClone(source.transform);
            product.MatterState = new ItemMatterState
            {
                Initialized = true,
                TemperatureCelsius = source.MatterState.TemperatureCelsius,
                Moisture = definition.Matter?.InitialMoisture ?? 0f
            };
            return AppendTransitionOutputs(product, outputs, path, depth + 1);
        }
    }

    private static void MergeOutput(List<ItemData> outputs, ItemData product)
    {
        foreach (ItemData existing in outputs)
            if (string.Equals(existing.IDName, product.IDName, StringComparison.Ordinal) &&
                Mathf.Approximately(existing.MatterState.TemperatureCelsius, product.MatterState.TemperatureCelsius))
            { existing.Stack.Amount += product.Stack.Amount; return; }
        outputs.Add(product);
    }

    public static bool TryApplySolidTransition(Inventory inventory, int slotIndex, string processId)
        => TransformSolidSlot(inventory, inventory, slotIndex, processId).Success;

    public static CraftingResult TransformSolidSlot(Inventory inventory, Inventory output, int slotIndex, string processId)
    {
        if (inventory?.Data?.itemSlots == null || (uint)slotIndex >= (uint)inventory.Data.itemSlots.Count)
            return CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "物质转化槽位无效");
        ItemData source = inventory.Data.itemSlots[slotIndex]?.itemData;
        if (!TryCreateSolidTransitionOutputs(source, out IReadOnlyList<ItemData> outputs))
            return CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "材料尚未满足自身转化条件");
        var match = new CraftingRecipeMatch(new RuntimeRecipe { Id = processId }, false,
            new[] { new CraftingConsumption(slotIndex, source.Stack.Amount) });
        return CraftingService.CommitMaterialOutputs(inventory, output, match, outputs);
    }

    public static float GetMinimumConsumedTemperature(Inventory inventory, CraftingRecipeMatch match, float fallback)
    {
        if (inventory?.Data?.itemSlots == null || match?.Consumptions == null || match.Consumptions.Count == 0)
            return fallback;
        float minimum = float.MaxValue;
        bool found = false;
        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if ((uint)consumption.SlotIndex >= (uint)inventory.Data.itemSlots.Count) continue;
            ItemData item = inventory.Data.itemSlots[consumption.SlotIndex]?.itemData;
            if (item?.MatterState?.Initialized != true) continue;
            minimum = Mathf.Min(minimum, item.MatterState.TemperatureCelsius);
            found = true;
        }
        return found ? minimum : fallback;
    }

    private static RuntimeItemMatterDefinition ResolveMatter(ItemData item)
    {
        GameRes resources = GameRes.ExistingInstance;
        return item != null && resources != null && !string.IsNullOrWhiteSpace(item.IDName) &&
               resources.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition)
            ? definition.Matter
            : null;
    }
}

/// <summary>材料自己根据温度完成热转化与接触反应，不保存炉体加工状态。</summary>
public static class MaterialThermalProcessor
{
    #region 材料热反应

    public static bool Process(Inventory input, Inventory output, Player actor = null,
        ISet<int> blockedMaterialSlots = null, ISet<int> rejectedMaterialSlots = null)
    {
        if (input?.Data?.itemSlots == null || output?.Data?.itemSlots == null ||
            MachineInventory.IsBeingDragged(input) || MachineInventory.IsBeingDragged(output)) return false;
        bool changed = false;
        using var blockedLease = UnityEngine.Pool.HashSetPool<int>.Get(out var blockedSlots);
        if (blockedMaterialSlots != null) blockedSlots.UnionWith(blockedMaterialSlots);
        // 多材料反应先于单材料变化，防止铜锡和铁碳被提前拆开处理。
        foreach (RuntimeItemReactionDefinition reaction in ItemReactionResolver.GetCandidates(input, liquid: false))
        {
            if (!ItemReactionResolver.TryMatchBatch(input, reaction, out CraftingRecipeMatch match, out int batches, blockedSlots)) continue;
            var outputs = new List<ItemData>();
            float temperature = ItemMatterRuntime.GetMinimumConsumedTemperature(input, match, 0f);
            foreach (RuntimeRecipeResult result in reaction.Recipe.outputs.results)
            {
                ItemData product = GameRes.ExistingInstance.CreateItemData(result.ItemName);
                product.Stack.Amount = result.amount * (float)batches;
                CraftedDurabilityQuality.ApplyToCraftedOutput(product, result.durabilityMultiplier);
                GameRes.ExistingInstance.TryGetItemDefinition(product.IDName, out RuntimeItemDefinition definition);
                product.MatterState = new ItemMatterState
                {
                    Initialized = true,
                    TemperatureCelsius = temperature,
                    Moisture = definition.Matter?.InitialMoisture ?? 0f
                };
                if (ItemMatterRuntime.TryCreateSolidTransitionOutputs(product, out IReadOnlyList<ItemData> transformed))
                    outputs.AddRange(transformed);
                else outputs.Add(product);
            }
            CraftingResult committed = CraftingService.CommitMaterialOutputs(input, output, match, outputs);
            if (!committed.Success)
            {
                foreach (CraftingConsumption consumption in match.Consumptions)
                {
                    blockedSlots.Add(consumption.SlotIndex);
                    rejectedMaterialSlots?.Add(consumption.SlotIndex);
                }
                continue;
            }
            changed = true;
            Publish(actor, committed);
        }
        for (int index = 0; index < input.Data.itemSlots.Count; index++)
        {
            if (blockedSlots.Contains(index) ||
                ItemMatterRuntime.GetMatchedTransition(input.Data.itemSlots[index]?.itemData, liquid: false) == null) continue;
            CraftingResult result = ItemMatterRuntime.TransformSolidSlot(input, output, index, "matter.thermal");
            if (!result.Success) { rejectedMaterialSlots?.Add(index); continue; }
            changed = true;
            Publish(actor, result);
        }
        return changed;
    }

    private static void Publish(Player actor, CraftingResult result)
    {
        foreach (ItemData output in result.Outputs)
            FlatWorld.Gameplay.Progress.GameplayProgressEvents.PublishSmeltSucceeded(actor, output.IDName, output.Stack.Amount);
    }

    #endregion
}

public static class ItemReactionResolver
{
    public static IReadOnlyList<RuntimeItemReactionDefinition> GetCandidates(Inventory input, bool liquid)
    {
        var candidates = new List<RuntimeItemReactionDefinition>();
        GameRes resources = GameRes.ExistingInstance;
        if (input?.Data?.itemSlots == null || resources == null) return candidates;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            ItemData item = slot?.itemData;
            if (item == null || !resources.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition)) continue;
            foreach (RuntimeItemReactionDefinition reaction in definition.Reactions)
                if (reaction != null && (reaction.LiquidOutput != null) == liquid && seen.Add(reaction.Signature))
                    candidates.Add(reaction);
        }
        return candidates.OrderByDescending(reaction => reaction.Recipe.inputs.RowItems_List
                .Select(ingredient => ingredient.matchMode == MatchMode.ByTag ? "tag:" + ingredient.Tag : ingredient.ItemName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .ThenByDescending(reaction => reaction.MinTemperature ?? float.MinValue)
            .ThenBy(reaction => reaction.Id, StringComparer.Ordinal).ToList();
    }

    // 每份实际参与反应的材料都必须达到条件，较冷的同类材料留在原槽。
    public static bool TryMatch(Inventory input, RuntimeItemReactionDefinition reaction, out CraftingRecipeMatch match)
        => TryMatchBatch(input, reaction, out match, out _, maxBatches: 1);

    public static bool TryMatchBatch(Inventory input, RuntimeItemReactionDefinition reaction,
        out CraftingRecipeMatch match, out int batches, ISet<int> blockedSlots = null, int maxBatches = int.MaxValue)
    {
        match = null;
        batches = 0;
        if (input?.Data?.itemSlots == null || reaction == null || maxBatches < 1) return false;
        var eligible = new List<ItemSlot>();
        for (int index = 0; index < input.Data.itemSlots.Count; index++)
        {
            ItemSlot slot = input.Data.itemSlots[index];
            if (blockedSlots?.Contains(index) != true && slot?.itemData?.MatterState?.Initialized == true &&
                !input.IsSlotBeingDragged(index) && reaction.TemperatureMatches(slot.itemData.MatterState.TemperatureCelsius))
                eligible.Add(slot);
            else eligible.Add(null);
        }
        IReadOnlyList<RuntimeRecipeIngredient> ingredients = reaction.Recipe.inputs.RowItems_List;
        int upper = maxBatches;
        foreach (RuntimeRecipeIngredient ingredient in ingredients)
        {
            double available = 0d;
            foreach (ItemSlot slot in eligible)
                if (CraftingIngredientMatcher.MatchesIdentity(ingredient, slot?.itemData))
                    available += slot.itemData.Stack.Amount;
            // 批次数和配方用量始终按整数计算，先约束乘积再分配材料。
            upper = Math.Min(upper, int.MaxValue / ingredient.amount);
            upper = (int)Math.Min(upper, Math.Floor(available / ingredient.amount));
        }
        if (upper < 1) return false;
        List<CraftingConsumption> best = null;
        int low = 1;
        while (low <= upper)
        {
            int candidate = low + (upper - low) / 2;
            var scaled = new List<RuntimeRecipeIngredient>();
            foreach (RuntimeRecipeIngredient ingredient in ingredients)
                scaled.Add(new RuntimeRecipeIngredient
                {
                    matchMode = ingredient.matchMode,
                    ItemName = ingredient.ItemName,
                    Tag = ingredient.Tag,
                    amount = checked(ingredient.amount * candidate)
                });
            if (CraftingMaterialAllocator.TryAllocate(eligible, scaled, out List<CraftingConsumption> consumptions))
            {
                batches = candidate; best = consumptions;
                if (candidate == int.MaxValue) break;
                low = candidate + 1;
            }
            else upper = candidate - 1;
        }
        if (best == null) return false;
        match = new CraftingRecipeMatch(reaction.Recipe, false, best);
        return true;
    }

    public static bool TryResolve(Inventory input, CraftingCapabilities capabilities,
        out RuntimeItemReactionDefinition reaction, out CraftingRecipeMatch match, bool requireLiquidOutput = false)
    {
        reaction = null;
        match = null;
        foreach (RuntimeItemReactionDefinition candidate in GetCandidates(input, requireLiquidOutput))
            if (TryMatch(input, candidate, out match)) { reaction = candidate; return true; }
        return false;
    }
}

#endregion
