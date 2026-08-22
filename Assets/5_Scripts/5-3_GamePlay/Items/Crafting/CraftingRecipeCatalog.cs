using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 运行时配方目录；统一维护 ID 索引与按类型排序的候选列表。
/// 注册时完成排序，预览阶段只遍历稳定只读列表，避免反复扫描和排序全量字典。
/// </summary>
public sealed class CraftingRecipeCatalog
{
    private static readonly IReadOnlyList<RuntimeRecipe> EmptyRecipes = Array.Empty<RuntimeRecipe>();

    private readonly Dictionary<string, RuntimeRecipe> recipesById =
        new Dictionary<string, RuntimeRecipe>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<RecipeType, List<RuntimeRecipe>> recipesByType =
        new Dictionary<RecipeType, List<RuntimeRecipe>>();

    public IReadOnlyDictionary<string, RuntimeRecipe> RecipesById => recipesById;
    public int Count => recipesById.Count;

    /// <summary>清空全部运行时索引。</summary>
    public void Clear()
    {
        recipesById.Clear();
        recipesByType.Clear();
    }

    /// <summary>注册一条已校验配方，并更新对应类型的稳定候选顺序。</summary>
    public void Register(RuntimeRecipe recipe)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id))
            throw new InvalidDataException("注册的运行时配方或配方 ID 为空");
        if (recipe.inputs == null)
            throw new InvalidDataException($"配方 {recipe.Id} 缺少输入定义");
        if (recipesById.ContainsKey(recipe.Id))
            throw new InvalidDataException($"配方 ID 冲突：{recipe.Id}");

        recipesById.Add(recipe.Id, recipe);
        if (!recipesByType.TryGetValue(recipe.inputs.recipeType, out List<RuntimeRecipe> candidates))
        {
            candidates = new List<RuntimeRecipe>();
            recipesByType.Add(recipe.inputs.recipeType, candidates);
        }

        candidates.Add(recipe);
        candidates.Sort(ComparePriority);
    }

    /// <summary>按 ID 查询配方。</summary>
    public bool TryGet(string recipeId, out RuntimeRecipe recipe)
    {
        recipe = null;
        return !string.IsNullOrWhiteSpace(recipeId) && recipesById.TryGetValue(recipeId, out recipe);
    }

    /// <summary>返回已按匹配优先级排序的指定类型配方。</summary>
    public IReadOnlyList<RuntimeRecipe> GetByType(RecipeType recipeType)
    {
        return recipesByType.TryGetValue(recipeType, out List<RuntimeRecipe> candidates)
            ? candidates
            : EmptyRecipes;
    }

    #region 候选优先级

    /// <summary>更具体的配方排在前面：有序、占用更多、精确物品更多，最后按 ID 稳定排序。</summary>
    private static int ComparePriority(RuntimeRecipe left, RuntimeRecipe right)
    {
        int comparison = IsOrdered(right).CompareTo(IsOrdered(left));
        if (comparison != 0)
            return comparison;

        comparison = CountIngredients(right).CompareTo(CountIngredients(left));
        if (comparison != 0)
            return comparison;

        comparison = CountExactIngredients(right).CompareTo(CountExactIngredients(left));
        if (comparison != 0)
            return comparison;

        comparison = string.Compare(left?.Id, right?.Id, StringComparison.OrdinalIgnoreCase);
        return comparison != 0
            ? comparison
            : string.Compare(left?.Id, right?.Id, StringComparison.Ordinal);
    }

    private static bool IsOrdered(RuntimeRecipe recipe)
    {
        return recipe?.inputs?.inputOrder == RecipeInputRule.规则合成;
    }

    private static int CountIngredients(RuntimeRecipe recipe)
    {
        IReadOnlyList<RuntimeRecipeIngredient> ingredients = recipe?.inputs?.RowItems_List;
        if (ingredients == null)
            return 0;

        int count = 0;
        for (int index = 0; index < ingredients.Count; index++)
        {
            if (!CraftingIngredientMatcher.IsEmpty(ingredients[index]))
                count++;
        }

        return count;
    }

    private static int CountExactIngredients(RuntimeRecipe recipe)
    {
        IReadOnlyList<RuntimeRecipeIngredient> ingredients = recipe?.inputs?.RowItems_List;
        if (ingredients == null)
            return 0;

        int count = 0;
        for (int index = 0; index < ingredients.Count; index++)
        {
            RuntimeRecipeIngredient ingredient = ingredients[index];
            if (!CraftingIngredientMatcher.IsEmpty(ingredient) && ingredient.matchMode == MatchMode.ExactItem)
                count++;
        }

        return count;
    }

    #endregion
}
