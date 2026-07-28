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
    public RuntimeRecipeInput inputs = new RuntimeRecipeInput();
    public RuntimeRecipeOutput outputs = new RuntimeRecipeOutput();
    public bool enableMirrorCrafting = true;
    public List<RuntimeRecipeAction> action = new List<RuntimeRecipeAction>();
    public float Temperature;
    public float Temperature_Max = 2000f;

    public string name => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

public sealed class RuntimeRecipeInput
{
    public List<RuntimeRecipeIngredient> RowItems_List = new List<RuntimeRecipeIngredient>();
    public RecipeType recipeType = RecipeType.Crafting;
    public RecipeInputRule inputOrder = RecipeInputRule.规则合成;
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

public sealed class RuntimeRecipeResult
{
    public string ItemName = string.Empty;
    public int amount = 1;
}

public sealed class RuntimeRecipeAction
{
    public string Type;
    public string TargetRole;
    public float Value;
    public int SlotIndex = -1;
}
