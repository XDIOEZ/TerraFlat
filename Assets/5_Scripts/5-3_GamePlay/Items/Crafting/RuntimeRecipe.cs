using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 校验后的运行时配方，不持有 ScriptableObject、Prefab 或其他 Unity 对象引用。
/// </summary>
public sealed class RuntimeRecipe
{
    public string Id;
    public string DisplayName;
    public string RequiredStation = string.Empty;
    public RuntimeRecipeInput inputs = new RuntimeRecipeInput();
    public RuntimeRecipeOutput outputs = new RuntimeRecipeOutput();
    public bool enableMirrorCrafting;
    public List<RuntimeRecipeAction> action = new List<RuntimeRecipeAction>();
    public float Temperature;
    public float Temperature_Max = 2000f;
    public float ProcessingSeconds;
    public int ManualWorkSteps = 1;
    public RuntimeLiquidOutput LiquidOutput;

    public string name => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

public sealed class RuntimeRecipeInput
{
    public List<RuntimeRecipeIngredient> RowItems_List = new List<RuntimeRecipeIngredient>();
    public RecipeType recipeType = RecipeType.Crafting;
    public RecipeInputRule inputOrder = RecipeInputRule.无规则合成;
    public int GridWidth;
    public int GridHeight;

    public override string ToString()
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
            return $"[][{recipeType}]";

        List<RuntimeRecipeIngredient> items = new List<RuntimeRecipeIngredient>(RowItems_List);
        if (inputOrder == RecipeInputRule.无规则合成)
        {
            items.Sort((left, right) =>
            {
                int modeComparison = left.matchMode.CompareTo(right.matchMode);
                if (modeComparison != 0)
                    return modeComparison;

                string leftKey = left.matchMode == MatchMode.ExactItem ? left.ItemName : left.Tag;
                string rightKey = right.matchMode == MatchMode.ExactItem ? right.ItemName : right.Tag;
                return string.Compare(leftKey, rightKey, StringComparison.Ordinal);
            });
        }

        return $"{string.Join(",", items.Select(ingredient => ingredient.ToString()))}[{recipeType}]";
    }
}

public sealed class RuntimeRecipeIngredient
{
    public MatchMode matchMode = MatchMode.ExactItem;
    public string ItemName = string.Empty;
    public string Tag = string.Empty;
    public int amount;

    public override string ToString()
    {
        return matchMode == MatchMode.ByTag ? Tag ?? string.Empty : ItemName ?? string.Empty;
    }

    public static implicit operator CraftingIngredient(RuntimeRecipeIngredient ingredient)
    {
        if (ingredient == null)
            return null;

        return new CraftingIngredient
        {
            matchMode = ingredient.matchMode,
            ItemName = ingredient.ItemName,
            Tag = ingredient.Tag,
            amount = ingredient.amount
        };
    }
}

public sealed class RuntimeRecipeOutput
{
    public List<RuntimeRecipeResult> results = new List<RuntimeRecipeResult>();
}

public sealed class RuntimeLiquidOutput
{
    public string LiquidId = string.Empty;
    public float Amount = 1f;
}

public sealed class RuntimeRecipeResult
{
    public string ItemName = string.Empty;
    public int amount = 1;
    public float durabilityMultiplier = CraftedDurabilityQuality.DefaultMultiplier;
}

public sealed class RuntimeRecipeAction
{
    public string Type;
    public string TargetRole;
    public float Value;
}

/// <summary>把物品自身的加工响应适配成现有制作事务；设备只提供 capability，不持有具体物品配方。</summary>
public static class ItemProcessingResolver
{
    public static bool TryResolve(
        ItemData itemData,
        string capability,
        out RuntimeItemProcessingDefinition processing)
        => TryResolve(itemData, capability, null, out processing);

    public static bool TryResolve(
        ItemData itemData,
        string capability,
        int? sourceLevel,
        out RuntimeItemProcessingDefinition processing)
    {
        processing = null;
        GameRes resources = GameRes.ExistingInstance;
        if (itemData == null || resources == null || string.IsNullOrWhiteSpace(itemData.IDName) ||
            !resources.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
        {
            return false;
        }

        if (!definition.TryGetProcessing(capability, out processing))
            return false;
        if (processing.AcceptsSourceLevel(sourceLevel))
            return true;
        processing = null;
        return false;
    }

    public static bool TryGetSourceLevel(ItemData source, string capability, out int level)
    {
        level = 0;
        GameRes resources = GameRes.ExistingInstance;
        return source != null && resources != null && !string.IsNullOrWhiteSpace(source.IDName) &&
               resources.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition) &&
               definition.TryGetProcessingCapabilityLevel(capability, out level);
    }

    public static bool TryResolve(
        ItemData itemData,
        ItemData source,
        string capability,
        out RuntimeItemProcessingDefinition processing)
    {
        processing = null;
        return TryGetSourceLevel(source, capability, out int level) &&
               TryResolve(itemData, capability, level, out processing);
    }

    /// <summary>从当前库存中寻找第一份真正可提交的物品加工响应，允许产物继续留在同一容器。</summary>
    public static bool TryResolveRecipe(
        Inventory input,
        string capability,
        CraftingCapabilities capabilities,
        out RuntimeItemProcessingDefinition processing,
        int? sourceLevel = null)
    {
        processing = null;
        if (input?.Data?.itemSlots == null || capabilities == null || string.IsNullOrWhiteSpace(capability))
            return false;

        for (int index = 0; index < input.Data.itemSlots.Count; index++)
        {
            ItemData itemData = input.Data.itemSlots[index]?.itemData;
            if (!TryResolve(itemData, capability, sourceLevel, out RuntimeItemProcessingDefinition candidate))
                continue;
            if (!CraftingRecipeMatcher.TryMatchRecipe(input, candidate.Recipe, capabilities, out _))
                continue;

            processing = candidate;
            return true;
        }

        return false;
    }
}
