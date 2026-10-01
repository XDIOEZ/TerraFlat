using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

#region 配置数据

[Serializable]
public sealed class ItemMatterDefinitionDto
{
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
public sealed class ItemMatterTransitionDto
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

    [JsonProperty("outputAmountMultiplier")]
    public float OutputAmountMultiplier = 1f;

    [JsonProperty("liquidOutput", NullValueHandling = NullValueHandling.Ignore)]
    public ItemReactionLiquidOutputDto LiquidOutput;
}

[Serializable]
public sealed class ItemReactionDefinitionDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("minTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MinTemperature;

    [JsonProperty("maxTemperature", NullValueHandling = NullValueHandling.Ignore)]
    public float? MaxTemperature;

    [JsonProperty("work")]
    public float Work = 100f;

    [JsonProperty("inputs")]
    public List<ItemReactionIngredientDto> Inputs = new();

    [JsonProperty("outputs")]
    public List<ItemReactionOutputDto> Outputs = new();

    [JsonProperty("liquidOutput", NullValueHandling = NullValueHandling.Ignore)]
    public ItemReactionLiquidOutputDto LiquidOutput;
}

[Serializable]
public sealed class ItemReactionIngredientDto
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
public sealed class ItemReactionOutputDto
{
    [JsonProperty("itemId")]
    public string ItemId;

    [JsonProperty("amount")]
    public int Amount = 1;

    [JsonProperty("durabilityMultiplier", NullValueHandling = NullValueHandling.Ignore)]
    public float? DurabilityMultiplier;
}

[Serializable]
public sealed class ItemReactionLiquidOutputDto
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
    public float InitialMoisture { get; }
    public float EvaporationRatePerSecond { get; }
    public float EvaporationStartTemperature { get; }
    public float TemperatureDryingMultiplierPer10C { get; }
    public float WaterAbsorptionRatePerSecond { get; }
    public IReadOnlyList<RuntimeItemMatterTransition> Transitions { get; }

    public RuntimeItemMatterDefinition(
        float initialMoisture,
        float evaporationRatePerSecond,
        float evaporationStartTemperature,
        float temperatureDryingMultiplierPer10C,
        float waterAbsorptionRatePerSecond,
        IReadOnlyList<RuntimeItemMatterTransition> transitions)
    {
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
    public RuntimeLiquidOutput LiquidOutput { get; }

    public RuntimeItemMatterTransition(
        string id,
        float? minTemperature,
        float? maxTemperature,
        float? minMoisture,
        float? maxMoisture,
        string outputItemId,
        float outputAmountMultiplier,
        RuntimeLiquidOutput liquidOutput)
    {
        Id = id;
        MinTemperature = minTemperature;
        MaxTemperature = maxTemperature;
        MinMoisture = minMoisture;
        MaxMoisture = maxMoisture;
        OutputItemId = outputItemId;
        OutputAmountMultiplier = outputAmountMultiplier;
        LiquidOutput = liquidOutput;
    }

    public bool Matches(ItemMatterState state)
    {
        if (state == null || !state.Initialized) return false;
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
    public float WorkRequired { get; }
    public RuntimeRecipe Recipe { get; }
    public string Signature { get; }
    public RuntimeLiquidOutput LiquidOutput => Recipe.LiquidOutput;

    public RuntimeItemReactionDefinition(
        string sourceItemId,
        string id,
        float? minTemperature,
        float? maxTemperature,
        float workRequired,
        RuntimeRecipe recipe,
        string signature)
    {
        SourceItemId = sourceItemId;
        Id = id;
        MinTemperature = minTemperature;
        MaxTemperature = maxTemperature;
        WorkRequired = workRequired;
        Recipe = recipe;
        Signature = signature;
    }

    public bool TemperatureMatches(float temperature)
    {
        return (!MinTemperature.HasValue || temperature >= MinTemperature.Value) &&
               (!MaxTemperature.HasValue || temperature <= MaxTemperature.Value);
    }
}

#endregion

#region 编译与去重

public static class ItemMatterReactionCompiler
{
    public static RuntimeItemMatterDefinition CompileMatter(ItemMatterDefinitionDto source, string itemId)
    {
        if (source == null) return null;
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
            string outputId = transition.OutputItemId?.Trim();
            RuntimeLiquidOutput liquid = CompileLiquid(transition.LiquidOutput, itemId, "matter transition");
            if (string.IsNullOrWhiteSpace(outputId) == (liquid == null))
                throw new InvalidDataException($"物品 {itemId} 的 matter transition 必须且只能声明一种固体/液体产物");
            transitions.Add(new RuntimeItemMatterTransition(
                string.IsNullOrWhiteSpace(transition.Id) ? $"matter.{itemId}.{transitions.Count}" : transition.Id.Trim(),
                transition.MinTemperature, transition.MaxTemperature,
                transition.MinMoisture, transition.MaxMoisture,
                outputId, transition.OutputAmountMultiplier, liquid));
        }

        return new RuntimeItemMatterDefinition(
            source.InitialMoisture,
            source.EvaporationRatePerSecond,
            source.EvaporationStartTemperature,
            source.TemperatureDryingMultiplierPer10C,
            source.WaterAbsorptionRatePerSecond,
            transitions.AsReadOnly());
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
            if (!IsFinite(definition.Work) || definition.Work <= 0f)
                throw new InvalidDataException($"物品 {itemId} 的 reaction.work 必须为正数");
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
                ProcessingSeconds = definition.Work,
                ManualWorkSteps = Mathf.Max(1, Mathf.CeilToInt(definition.Work)),
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
                definition.Work, recipe, signature));
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
        return $"T:{F(definition.MinTemperature)}:{F(definition.MaxTemperature)};W:{F(definition.Work)};I:{inputKey};O:{outputKey};L:{liquidKey}";
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
    public static bool Advance(ItemData item, float ambientTemperature, float airExposure, float seconds, bool submerged = false)
    {
        if (item == null || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
        ItemMatterState state = item.MatterState ??= new ItemMatterState();
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.TemperatureCelsius = ambientTemperature;
            state.Moisture = matter?.InitialMoisture ?? 0f;
        }

        float exposure = Mathf.Max(0f, airExposure);
        float beforeTemperature = state.TemperatureCelsius;
        float beforeMoisture = state.Moisture;
        float conduction = Mathf.Max(0f, item.HeatConductionRate) * Mathf.Max(0.01f, exposure);
        state.TemperatureCelsius = Mathf.MoveTowards(state.TemperatureCelsius, ambientTemperature, conduction * seconds);

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

        return !Mathf.Approximately(beforeTemperature, state.TemperatureCelsius) ||
               !Mathf.Approximately(beforeMoisture, state.Moisture);
    }

    public static RuntimeItemMatterTransition GetMatchedTransition(ItemData item, bool liquid)
    {
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        if (matter == null || item?.MatterState == null) return null;
        foreach (RuntimeItemMatterTransition transition in matter.Transitions)
            if ((transition.LiquidOutput != null) == liquid && transition.Matches(item.MatterState))
                return transition;
        return null;
    }

    public static float GetMoistureProgress01(ItemData item)
    {
        RuntimeItemMatterDefinition matter = ResolveMatter(item);
        if (matter == null || item?.MatterState?.Initialized != true) return 0f;
        foreach (RuntimeItemMatterTransition transition in matter.Transitions)
        {
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

    public static bool TryCreateSolidTransitionReplacement(ItemData source, out ItemData replacement)
    {
        replacement = null;
        RuntimeItemMatterTransition transition = GetMatchedTransition(source, liquid: false);
        GameRes resources = GameRes.ExistingInstance;
        if (transition == null || resources == null || string.IsNullOrWhiteSpace(transition.OutputItemId)) return false;
        replacement = resources.CreateItemData(transition.OutputItemId);
        if (replacement?.Stack == null || source?.Stack == null) { replacement = null; return false; }
        replacement.Guid = source.Guid;
        replacement.inHand = source.inHand;
        replacement.Stack.Amount = Mathf.Max(0f, source.Stack.Amount * transition.OutputAmountMultiplier);
        replacement.Stack.CanBePickedUp = source.Stack.CanBePickedUp;
        replacement.transform = FastCloner.FastCloner.DeepClone(source.transform);
        return true;
    }

    public static bool TryApplySolidTransition(Inventory inventory, int slotIndex, string processId)
    {
        if (inventory?.Data?.itemSlots == null || (uint)slotIndex >= (uint)inventory.Data.itemSlots.Count) return false;
        ItemData source = inventory.Data.itemSlots[slotIndex]?.itemData;
        RuntimeItemMatterTransition transition = GetMatchedTransition(source, liquid: false);
        if (source?.Stack == null || transition == null || string.IsNullOrWhiteSpace(transition.OutputItemId)) return false;
        int outputAmount = Mathf.Max(0, Mathf.RoundToInt(source.Stack.Amount * transition.OutputAmountMultiplier));
        return CraftingService.TransformSlot(inventory, slotIndex, transition.OutputItemId, outputAmount, processId).Success;
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

public static class ItemReactionResolver
{
    public static bool TryResolve(
        Inventory input,
        CraftingCapabilities capabilities,
        out RuntimeItemReactionDefinition reaction,
        out CraftingRecipeMatch match,
        bool requireLiquidOutput = false)
    {
        reaction = null;
        match = null;
        GameRes resources = GameRes.ExistingInstance;
        if (input?.Data?.itemSlots == null || resources == null || capabilities == null) return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            ItemData item = slot?.itemData;
            if (item == null || !resources.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition)) continue;
            foreach (RuntimeItemReactionDefinition candidate in definition.Reactions)
            {
                if (candidate == null || !seen.Add(candidate.Signature)) continue;
                if (requireLiquidOutput != (candidate.LiquidOutput != null)) continue;
                if (!CraftingRecipeMatcher.TryMatchRecipe(input, candidate.Recipe, capabilities, out CraftingRecipeMatch candidateMatch))
                    continue;
                reaction = candidate;
                match = candidateMatch;
                return true;
            }
        }
        return false;
    }
}

#endregion
