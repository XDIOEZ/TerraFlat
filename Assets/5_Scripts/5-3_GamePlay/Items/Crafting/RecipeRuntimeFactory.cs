using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// 负责 DTO 校验、规范化并创建不可依赖 Unity 资源引用的运行时配方。
/// </summary>
public static class RecipeRuntimeFactory
{
    public const int SupportedSchemaVersion = 1;

    public static List<RuntimeRecipe> BuildCatalog(
        RecipeCatalogDto catalog,
        Func<string, bool> itemExists,
        out List<string> warnings)
    {
        warnings = new List<string>();
        if (catalog == null)
            throw new InvalidDataException("配方 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的配方 schemaVersion：{catalog.SchemaVersion}");

        var result = new List<RuntimeRecipe>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecipeDto dto in catalog.Recipes ?? Enumerable.Empty<RecipeDto>())
        {
            RuntimeRecipe recipe = Build(dto, itemExists, warnings);
            if (!ids.Add(recipe.Id))
                throw new InvalidDataException($"存在重复配方 ID：{recipe.Id}");
            result.Add(recipe);
        }

        return result;
    }

    public static RuntimeRecipe Build(RecipeDto dto, Func<string, bool> itemExists, List<string> warnings = null)
    {
        if (dto == null)
            throw new InvalidDataException("配方定义为空");

        string id = NormalizeRequired(dto.Id, "配方 id");
        RecipeType recipeType = ParseRecipeType(dto.RecipeType, id);
        RecipeInputRule inputRule = ParseInputRule(dto.InputRule, id);
        if (recipeType == RecipeType.Crafting && inputRule != RecipeInputRule.无规则合成)
            throw new InvalidDataException($"配方 {id} 是普通合成，inputRule 必须为 unordered");
        if (dto.ManualWorkSteps <= 0)
            throw new InvalidDataException($"配方 {id} 的 manualWorkSteps 必须大于 0");
        RuntimeRecipeInput inputs = recipeType == RecipeType.Crafting
            ? BuildCraftingInputs(dto, id, itemExists, warnings)
            : BuildHeatingInputs(dto, id, inputRule, itemExists, warnings);

        var recipe = new RuntimeRecipe
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName.Trim(),
            RequiredStation = string.IsNullOrWhiteSpace(dto.RequiredStation)
                ? string.Empty
                : dto.RequiredStation.Trim(),
            enableMirrorCrafting = dto.AllowMirror == true,
            Temperature = dto.Temperature,
            Temperature_Max = dto.MaxTemperature,
            ProcessingSeconds = dto.ProcessingSeconds,
            ManualWorkSteps = dto.ManualWorkSteps,
            inputs = inputs
        };

        foreach (RecipeOutputDto output in dto.Outputs ?? Enumerable.Empty<RecipeOutputDto>())
        {
            if (output == null)
                continue;
            string itemId = NormalizeRequired(output.ItemId, $"配方 {id} 输出 itemId");
            if (output.Amount <= 0)
                throw new InvalidDataException($"配方 {id} 的输出 {itemId} 数量必须大于 0");
            float durabilityMultiplier = output.DurabilityMultiplier ?? CraftedDurabilityQuality.DefaultMultiplier;
            if (!CraftedDurabilityQuality.IsValidMultiplier(durabilityMultiplier))
                throw new InvalidDataException($"配方 {id} 的输出 {itemId} durabilityMultiplier 必须是大于 0 的有限数值");
            ValidateItemReference(itemId, itemExists, $"配方 {id} 输出", warnings);
            recipe.outputs.results.Add(new RuntimeRecipeResult
            {
                ItemName = itemId,
                amount = output.Amount,
                durabilityMultiplier = durabilityMultiplier
            });
        }
        if (dto.LiquidOutput != null)
        {
            string liquidId = NormalizeLiquidId(dto.LiquidOutput.LiquidId, $"配方 {id} liquidOutput.liquidId");
            if (float.IsNaN(dto.LiquidOutput.Amount) || float.IsInfinity(dto.LiquidOutput.Amount) ||
                dto.LiquidOutput.Amount <= 0f)
            {
                throw new InvalidDataException($"配方 {id} 的液体产出数量必须是大于 0 的有限数值");
            }
            if (recipe.ProcessingSeconds <= 0f || float.IsNaN(recipe.ProcessingSeconds) ||
                float.IsInfinity(recipe.ProcessingSeconds))
            {
                throw new InvalidDataException($"配方 {id} 的 processingSeconds 必须是大于 0 的有限数值");
            }

            recipe.LiquidOutput = new RuntimeLiquidOutput
            {
                LiquidId = liquidId,
                Amount = dto.LiquidOutput.Amount
            };
        }

        if (recipe.outputs.results.Count == 0 && recipe.LiquidOutput == null)
            throw new InvalidDataException($"配方 {id} 没有输出");

        foreach (RecipeActionDto action in dto.Actions ?? Enumerable.Empty<RecipeActionDto>())
        {
            if (action == null)
                continue;
            string type = NormalizeRequired(action.Type, $"配方 {id} action.type").ToLowerInvariant();
            if (!RecipeActionRunner.HasHandler(type))
                throw new InvalidDataException($"配方 {id} 使用了未知动作：{type}");
            if (type == RecipeActionRunner.ChangeDurabilityType && string.IsNullOrWhiteSpace(action.TargetRole))
                throw new InvalidDataException($"配方 {id} 的 change_durability 缺少 targetRole");
            if (type == RecipeActionRunner.ChangeDurabilityType && action.Value <= 0f)
                throw new InvalidDataException($"配方 {id} 的 change_durability.value 必须大于 0");
            if (action.LegacySlotIndex.HasValue)
                throw new InvalidDataException($"配方 {id} 的 action.slotIndex 已废弃，请用 targetRole 指定工具");

            recipe.action.Add(new RuntimeRecipeAction
            {
                Type = type,
                TargetRole = action.TargetRole?.Trim(),
                Value = action.Value
            });
        }

        if (recipeType == RecipeType.Smelting && recipe.Temperature_Max < recipe.Temperature)
            throw new InvalidDataException($"配方 {id} 的最高温度不能低于最低温度");
        return recipe;
    }

    #region 输入材料解析

    private static RuntimeRecipeInput BuildCraftingInputs(
        RecipeDto dto, string id, Func<string, bool> itemExists, List<string> warnings)
    {
        if (dto.GridWidth.HasValue || dto.GridHeight.HasValue || dto.AllowMirror.HasValue)
            throw new InvalidDataException($"普通合成配方 {id} 不再使用 gridWidth、gridHeight 或 allowMirror，只需列出材料和数量");

        List<RecipeIngredientDto> source = dto.Inputs ?? new List<RecipeIngredientDto>();
        if (source.Count == 0)
            throw new InvalidDataException($"普通合成配方 {id} 没有输入材料");

        var result = new RuntimeRecipeInput
        {
            recipeType = RecipeType.Crafting,
            inputOrder = RecipeInputRule.无规则合成
        };
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < source.Count; index++)
        {
            RecipeIngredientDto input = source[index];
            if (input?.Slot.HasValue == true)
                throw new InvalidDataException($"普通合成配方 {id} 的 inputs[{index}] 不再使用 slot，请按物品或标签填写总数量");
            RuntimeRecipeIngredient ingredient = ParseIngredient(
                input, id, $"材料 {index + 1}", itemExists, warnings);
            if (CraftingIngredientMatcher.IsEmpty(ingredient))
                throw new InvalidDataException($"普通合成配方 {id} 的材料 {index + 1} 没有物品或标签");
            string identity = ingredient.matchMode == MatchMode.ExactItem
                ? "item:" + ingredient.ItemName
                : "tag:" + ingredient.Tag;
            if (!identities.Add(identity))
                throw new InvalidDataException($"普通合成配方 {id} 重复列出 {identity}，请合并为一条材料并填写总数量");
            result.RowItems_List.Add(ingredient);
        }
        return result;
    }

    private static RuntimeRecipeInput BuildHeatingInputs(
        RecipeDto dto, string id, RecipeInputRule inputRule,
        Func<string, bool> itemExists, List<string> warnings)
    {
        List<RecipeIngredientDto> source = dto.Inputs ?? new List<RecipeIngredientDto>();
        int configuredWidth = dto.GridWidth.GetValueOrDefault();
        int configuredHeight = dto.GridHeight.GetValueOrDefault();
        int inputCount = source.Count == 0
            ? Math.Max(0, configuredWidth * configuredHeight)
            : source.Max(input => input?.Slot ?? -1) + 1;
        int width = configuredWidth > 0 ? configuredWidth : InferGridWidth(inputCount);
        int height = configuredHeight > 0 ? configuredHeight
            : width > 0 ? (int)Math.Ceiling((double)inputCount / width) : 0;
        int slotCount = Math.Max(inputCount, width * height);
        if (slotCount <= 0)
            throw new InvalidDataException($"热加工配方 {id} 没有输入槽");
        if (width <= 0 || height <= 0 || width * height != slotCount)
            throw new InvalidDataException($"热加工配方 {id} 的网格 {width}x{height} 与槽位数 {slotCount} 不一致");

        var result = new RuntimeRecipeInput
        {
            recipeType = RecipeType.Smelting,
            inputOrder = inputRule,
            GridWidth = width,
            GridHeight = height,
            RowItems_List = Enumerable.Range(0, slotCount).Select(_ => new RuntimeRecipeIngredient()).ToList()
        };
        var occupiedSlots = new HashSet<int>();
        foreach (RecipeIngredientDto input in source)
        {
            if (input == null)
                continue;
            if (!input.Slot.HasValue || input.Slot.Value < 0 || input.Slot.Value >= slotCount)
                throw new InvalidDataException($"热加工配方 {id} 的输入槽索引无效：{input.Slot}");
            int slot = input.Slot.Value;
            if (!occupiedSlots.Add(slot))
                throw new InvalidDataException($"热加工配方 {id} 重复定义输入槽：{slot}");
            result.RowItems_List[slot] = ParseIngredient(
                input, id, $"输入槽 {slot}", itemExists, warnings);
        }
        return result;
    }

    private static RuntimeRecipeIngredient ParseIngredient(
        RecipeIngredientDto input, string id, string location,
        Func<string, bool> itemExists, List<string> warnings)
    {
        if (input == null)
            throw new InvalidDataException($"配方 {id} 的{location}为空");
        MatchMode matchMode = ParseMatchMode(input.Match, id, location);
        string itemId = (input.ItemId ?? string.Empty).Trim();
        string tag = (input.Tag ?? string.Empty).Trim();
        if (input.Amount < 0)
            throw new InvalidDataException($"配方 {id} 的{location}数量不能小于 0");
        bool isEmpty = itemId.Length == 0 && tag.Length == 0 && input.Amount == 0;
        if (matchMode == MatchMode.ExactItem && !isEmpty)
        {
            if (itemId.Length == 0)
                throw new InvalidDataException($"配方 {id} 的{location}缺少 itemId");
            ValidateItemReference(itemId, itemExists, $"配方 {id} 输入", warnings);
        }
        else if (matchMode == MatchMode.ByTag && !isEmpty && tag.Length == 0)
        {
            throw new InvalidDataException($"配方 {id} 的{location}缺少 tag");
        }
        return new RuntimeRecipeIngredient
        {
            matchMode = matchMode,
            ItemName = itemId,
            Tag = tag,
            amount = input.Amount
        };
    }

    #endregion

    public static RecipeCatalogDto Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("配方 JSON 为空");
        return JsonConvert.DeserializeObject<RecipeCatalogDto>(json)
            ?? throw new InvalidDataException("配方 JSON 无法反序列化");
    }

    private static void ValidateItemReference(string itemId, Func<string, bool> itemExists, string context, List<string> warnings)
    {
        if (itemExists != null && !itemExists(itemId))
            warnings?.Add($"{context}引用的物品尚未注册：{itemId}");
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{field} 不能为空");
        return value.Trim();
    }

    private static string NormalizeLiquidId(string value, string field)
    {
        string normalized = NormalizeRequired(value, field);
        if (!normalized.Contains(':', StringComparison.Ordinal) || normalized.StartsWith(":", StringComparison.Ordinal) ||
            normalized.EndsWith(":", StringComparison.Ordinal) || normalized.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException($"{field} 必须是带稳定命名空间且不含空白的 ID：{normalized}");
        }
        return normalized;
    }

    private static RecipeType ParseRecipeType(string value, string id)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "crafting" => RecipeType.Crafting,
            "smelting" => RecipeType.Smelting,
            _ => throw new InvalidDataException($"配方 {id} 的 recipeType 无效：{value}")
        };
    }

    private static RecipeInputRule ParseInputRule(string value, string id)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ordered" => RecipeInputRule.规则合成,
            "unordered" => RecipeInputRule.无规则合成,
            _ => throw new InvalidDataException($"配方 {id} 的 inputRule 无效：{value}")
        };
    }

    private static MatchMode ParseMatchMode(string value, string id, string location)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => MatchMode.ExactItem,
            "exact_item" => MatchMode.ExactItem,
            "tag" => MatchMode.ByTag,
            _ => throw new InvalidDataException($"配方 {id} 的{location} match 无效：{value}")
        };
    }

    private static int InferGridWidth(int count)
    {
        if (count <= 0)
            return 0;
        int square = (int)Math.Round(Math.Sqrt(count));
        return square * square == count ? square : count;
    }
}
