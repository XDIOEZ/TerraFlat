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
        List<RecipeIngredientDto> sourceInputs = dto.Inputs ?? new List<RecipeIngredientDto>();
        int inputCount = sourceInputs.Count == 0 ? Math.Max(0, dto.GridWidth * dto.GridHeight) : sourceInputs.Max(input => input.Slot) + 1;
        int width = dto.GridWidth > 0 ? dto.GridWidth : InferGridWidth(inputCount);
        int height = dto.GridHeight > 0 ? dto.GridHeight : (width > 0 ? (int)Math.Ceiling((double)inputCount / width) : 0);
        int slotCount = Math.Max(inputCount, width * height);
        if (slotCount <= 0)
            throw new InvalidDataException($"配方 {id} 没有输入槽");
        if (width <= 0 || height <= 0 || width * height != slotCount)
            throw new InvalidDataException($"配方 {id} 的网格 {width}x{height} 与槽位数 {slotCount} 不一致");

        var recipe = new RuntimeRecipe
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName.Trim(),
            enableMirrorCrafting = dto.AllowMirror,
            Temperature = dto.Temperature,
            Temperature_Max = dto.MaxTemperature,
            inputs = new RuntimeRecipeInput
            {
                recipeType = recipeType,
                inputOrder = inputRule,
                GridWidth = width,
                GridHeight = height,
                RowItems_List = Enumerable.Range(0, slotCount).Select(_ => new RuntimeRecipeIngredient()).ToList()
            }
        };

        var occupiedSlots = new HashSet<int>();
        foreach (RecipeIngredientDto input in sourceInputs)
        {
            if (input == null)
                continue;
            if (input.Slot < 0 || input.Slot >= slotCount)
                throw new InvalidDataException($"配方 {id} 的输入槽索引越界：{input.Slot}");
            if (!occupiedSlots.Add(input.Slot))
                throw new InvalidDataException($"配方 {id} 重复定义输入槽：{input.Slot}");

            MatchMode matchMode = ParseMatchMode(input.Match, id, input.Slot);
            string itemId = (input.ItemId ?? string.Empty).Trim();
            string tag = (input.Tag ?? string.Empty).Trim();
            int amount = input.Amount;
            bool isEmpty = string.IsNullOrEmpty(itemId) && string.IsNullOrEmpty(tag) && amount <= 0;
            if (amount < 0)
                throw new InvalidDataException($"配方 {id} 的输入槽 {input.Slot} 数量不能小于 0");
            if (matchMode == MatchMode.ExactItem && !isEmpty)
            {
                if (string.IsNullOrWhiteSpace(itemId))
                    throw new InvalidDataException($"配方 {id} 的输入槽 {input.Slot} 缺少 itemId");
                ValidateItemReference(itemId, itemExists, $"配方 {id} 输入", warnings);
            }
            else if (matchMode == MatchMode.ByTag && !isEmpty && string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidDataException($"配方 {id} 的输入槽 {input.Slot} 缺少 tag");
            }

            recipe.inputs.RowItems_List[input.Slot] = new RuntimeRecipeIngredient
            {
                matchMode = matchMode,
                ItemName = itemId,
                Tag = tag,
                amount = Math.Max(0, amount)
            };
        }

        foreach (RecipeOutputDto output in dto.Outputs ?? Enumerable.Empty<RecipeOutputDto>())
        {
            if (output == null)
                continue;
            string itemId = NormalizeRequired(output.ItemId, $"配方 {id} 输出 itemId");
            if (output.Amount <= 0)
                throw new InvalidDataException($"配方 {id} 的输出 {itemId} 数量必须大于 0");
            ValidateItemReference(itemId, itemExists, $"配方 {id} 输出", warnings);
            recipe.outputs.results.Add(new RuntimeRecipeResult { ItemName = itemId, amount = output.Amount });
        }
        if (recipe.outputs.results.Count == 0)
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

            recipe.action.Add(new RuntimeRecipeAction
            {
                Type = type,
                TargetRole = action.TargetRole?.Trim(),
                Value = action.Value,
                SlotIndex = action.SlotIndex
            });
        }

        if (recipeType == RecipeType.Smelting && recipe.Temperature_Max < recipe.Temperature)
            throw new InvalidDataException($"配方 {id} 的最高温度不能低于最低温度");
        return recipe;
    }

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

    private static MatchMode ParseMatchMode(string value, string id, int slot)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => MatchMode.ExactItem,
            "exact_item" => MatchMode.ExactItem,
            "tag" => MatchMode.ByTag,
            _ => throw new InvalidDataException($"配方 {id} 的输入槽 {slot} match 无效：{value}")
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
