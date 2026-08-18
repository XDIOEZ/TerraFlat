using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 统一处理有序、无序、标签、镜像与大网格中的紧凑配方匹配。
/// </summary>
public static class CraftingRecipeMatcher
{
    public static bool TryMatch(
        Inventory inputInventory,
        CraftingCapabilities capabilities,
        out CraftingRecipeMatch match,
        out CraftingResult failure)
    {
        match = null;
        failure = null;

        if (inputInventory?.Data?.itemSlots == null || capabilities == null)
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "输入库存或制作能力为空");
            return false;
        }

        List<ItemSlot> inputSlots = GetInputSlots(inputInventory, capabilities.InputSlotLimit);
        if (inputSlots.Count == 0)
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "输入库存没有可用槽位");
            return false;
        }

        IEnumerable<RuntimeRecipe> recipes = GetCandidateRecipes(capabilities.RecipeType);
        foreach (RuntimeRecipe recipe in recipes)
        {
            if (TryMatchRecipe(inputSlots, recipe, capabilities, out match))
                return true;
        }

        failure = CraftingResult.Failed(CraftingFailureReason.RecipeNotFound, "当前输入没有匹配的配方");
        return false;
    }

    public static bool TryMatchRecipe(
        Inventory inputInventory,
        RuntimeRecipe recipe,
        CraftingCapabilities capabilities,
        out CraftingRecipeMatch match)
    {
        match = null;
        if (inputInventory?.Data?.itemSlots == null || capabilities == null)
            return false;

        List<ItemSlot> inputSlots = GetInputSlots(inputInventory, capabilities.InputSlotLimit);
        return TryMatchRecipe(inputSlots, recipe, capabilities, out match);
    }

    private static IEnumerable<RuntimeRecipe> GetCandidateRecipes(RecipeType recipeType)
    {
        if (GameRes.Instance?.recipeById == null)
            return Enumerable.Empty<RuntimeRecipe>();

        return GameRes.Instance.recipeById.Values
            .Where(recipe => recipe?.inputs != null && recipe.inputs.recipeType == recipeType)
            .OrderByDescending(recipe => CountRequiredIngredients(recipe.inputs.RowItems_List))
            .ThenBy(recipe => recipe.Id, StringComparer.Ordinal);
    }

    private static bool IsRecipeSupported(RuntimeRecipe recipe, CraftingCapabilities capabilities)
    {
        if (recipe?.inputs?.RowItems_List == null || recipe.inputs.RowItems_List.Count == 0)
            return false;
        if (capabilities.MaxRecipeWidth > 0 && recipe.inputs.GridWidth > capabilities.MaxRecipeWidth)
            return false;
        if (capabilities.MaxRecipeHeight > 0 && recipe.inputs.GridHeight > capabilities.MaxRecipeHeight)
            return false;
        return true;
    }

    private static bool TryMatchRecipe(
        IReadOnlyList<ItemSlot> inputSlots,
        RuntimeRecipe recipe,
        CraftingCapabilities capabilities,
        out CraftingRecipeMatch match)
    {
        match = null;
        if (!IsRecipeSupported(recipe, capabilities))
            return false;

        if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
        {
            return TryMatchOrdered(inputSlots, recipe, capabilities, false, out match) ||
                   recipe.enableMirrorCrafting && TryMatchOrdered(inputSlots, recipe, capabilities, true, out match);
        }

        return TryMatchUnordered(inputSlots, recipe, out match);
    }

    private static bool TryMatchOrdered(
        IReadOnlyList<ItemSlot> inputSlots,
        RuntimeRecipe recipe,
        CraftingCapabilities capabilities,
        bool mirrored,
        out CraftingRecipeMatch match)
    {
        match = null;
        int recipeWidth = recipe.inputs.GridWidth;
        int recipeHeight = recipe.inputs.GridHeight;
        if (recipeWidth <= 0 || recipeHeight <= 0 || recipeWidth * recipeHeight != recipe.inputs.RowItems_List.Count)
            return false;

        if (!TryGetInputGrid(inputSlots.Count, out int inputWidth, out int inputHeight))
            return false;

        if (inputWidth == recipeWidth && inputHeight == recipeHeight)
            return TryMatchOrderedAt(inputSlots, recipe, mirrored, 0, 0, inputWidth, out match);

        if (!capabilities.AllowCompactGrid || recipeWidth > inputWidth || recipeHeight > inputHeight)
            return false;

        if (!TryGetOccupiedBounds(inputSlots, inputWidth, out int minRow, out int maxRow, out int minColumn, out int maxColumn))
            return false;
        if (maxRow - minRow + 1 != recipeHeight || maxColumn - minColumn + 1 != recipeWidth)
            return false;

        return TryMatchOrderedAt(inputSlots, recipe, mirrored, minRow, minColumn, inputWidth, out match);
    }

    private static bool TryMatchOrderedAt(
        IReadOnlyList<ItemSlot> inputSlots,
        RuntimeRecipe recipe,
        bool mirrored,
        int rowOffset,
        int columnOffset,
        int inputWidth,
        out CraftingRecipeMatch match)
    {
        match = null;
        var consumptions = new List<CraftingConsumption>();
        for (int row = 0; row < recipe.inputs.GridHeight; row++)
        {
            for (int column = 0; column < recipe.inputs.GridWidth; column++)
            {
                int recipeColumn = mirrored ? recipe.inputs.GridWidth - 1 - column : column;
                int recipeIndex = row * recipe.inputs.GridWidth + recipeColumn;
                int inputIndex = (rowOffset + row) * inputWidth + columnOffset + column;
                if (inputIndex < 0 || inputIndex >= inputSlots.Count)
                    return false;

                RuntimeRecipeIngredient required = recipe.inputs.RowItems_List[recipeIndex];
                ItemData actual = inputSlots[inputIndex]?.itemData;
                if (!Matches(required, actual))
                    return false;
                if (required.amount > 0)
                    consumptions.Add(new CraftingConsumption(inputIndex, required.amount));
            }
        }

        if (!OutsideRegionIsEmpty(inputSlots, rowOffset, columnOffset, recipe.inputs.GridWidth, recipe.inputs.GridHeight, inputWidth))
            return false;

        match = new CraftingRecipeMatch(recipe, mirrored, consumptions);
        return true;
    }

    private static bool TryMatchUnordered(
        IReadOnlyList<ItemSlot> inputSlots,
        RuntimeRecipe recipe,
        out CraftingRecipeMatch match)
    {
        match = null;
        int requiredSlotCount = recipe.inputs.RowItems_List.Count(required => !IsEmptyIngredient(required));
        int occupiedSlotCount = inputSlots.Count(slot => slot?.itemData != null);
        // 无规则配方只要求材料总量满足；同一种材料可以集中在一个堆叠槽中，不能强制玩家先把它拆成多个槽位。
        // 但额外放入未声明的材料仍然要拒绝，避免误匹配后被事务消耗。
        if (requiredSlotCount == 0 || occupiedSlotCount > requiredSlotCount)
            return false;

        var remaining = new float[inputSlots.Count];
        for (int i = 0; i < inputSlots.Count; i++)
            remaining[i] = inputSlots[i]?.itemData?.Stack?.Amount ?? 0f;

        var consumptions = new Dictionary<int, float>();
        IEnumerable<RuntimeRecipeIngredient> requiredIngredients = recipe.inputs.RowItems_List
            .Where(required => !IsEmptyIngredient(required))
            .OrderBy(required => inputSlots.Count(slot => MatchesIdentity(required, slot?.itemData)))
            .ThenByDescending(required => required.amount);
        foreach (RuntimeRecipeIngredient required in requiredIngredients)
        {
            float need = required.amount;
            bool foundMatch = false;
            for (int i = 0; i < inputSlots.Count; i++)
            {
                ItemData itemData = inputSlots[i]?.itemData;
                if (!MatchesIdentity(required, itemData))
                    continue;

                foundMatch = true;
                if (need <= 0f)
                    continue;

                float consumed = Mathf.Min(need, remaining[i]);
                if (consumed <= 0f)
                    continue;

                remaining[i] -= consumed;
                need -= consumed;
                consumptions[i] = consumptions.TryGetValue(i, out float previous) ? previous + consumed : consumed;
                if (need <= 0f)
                    break;
            }

            if (!foundMatch || need > 0.0001f)
                return false;
        }

        foreach (ItemSlot slot in inputSlots)
        {
            if (slot?.itemData == null)
                continue;
            if (!recipe.inputs.RowItems_List.Any(required => !IsEmptyIngredient(required) && MatchesIdentity(required, slot.itemData)))
                return false;
        }

        match = new CraftingRecipeMatch(
            recipe,
            false,
            consumptions.Select(pair => new CraftingConsumption(pair.Key, pair.Value)).ToList());
        return true;
    }

    private static bool Matches(RuntimeRecipeIngredient required, ItemData actual)
    {
        if (IsEmptyIngredient(required))
            return actual == null;
        if (!MatchesIdentity(required, actual))
            return false;
        return required.amount <= 0f || actual.Stack != null && actual.Stack.Amount >= required.amount;
    }

    private static bool MatchesIdentity(RuntimeRecipeIngredient required, ItemData actual)
    {
        if (required == null || actual == null)
            return false;
        if (required.matchMode == MatchMode.ByTag)
        {
            string requiredTag = required.Tag?.Trim();
            return !string.IsNullOrEmpty(requiredTag) &&
                   actual.Tags != null &&
                   actual.Tags.Any(tag => string.Equals(tag?.Trim(), requiredTag, StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(actual.IDName?.Trim(), required.ItemName?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmptyIngredient(RuntimeRecipeIngredient ingredient)
    {
        return ingredient == null ||
               ingredient.amount <= 0 && string.IsNullOrWhiteSpace(ingredient.ItemName) && string.IsNullOrWhiteSpace(ingredient.Tag);
    }

    private static int CountRequiredIngredients(IEnumerable<RuntimeRecipeIngredient> ingredients)
    {
        return ingredients?.Count(ingredient => !IsEmptyIngredient(ingredient)) ?? 0;
    }

    private static List<ItemSlot> GetInputSlots(Inventory inventory, int slotLimit)
    {
        int count = slotLimit > 0
            ? Mathf.Min(slotLimit, inventory.Data.itemSlots.Count)
            : inventory.Data.itemSlots.Count;
        return inventory.Data.itemSlots.Take(count).ToList();
    }

    private static bool TryGetInputGrid(int count, out int width, out int height)
    {
        width = Mathf.RoundToInt(Mathf.Sqrt(count));
        height = width;
        return width > 0 && width * height == count;
    }

    private static bool TryGetOccupiedBounds(
        IReadOnlyList<ItemSlot> slots,
        int width,
        out int minRow,
        out int maxRow,
        out int minColumn,
        out int maxColumn)
    {
        minRow = int.MaxValue;
        maxRow = -1;
        minColumn = int.MaxValue;
        maxColumn = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.itemData == null)
                continue;
            int row = i / width;
            int column = i % width;
            minRow = Math.Min(minRow, row);
            maxRow = Math.Max(maxRow, row);
            minColumn = Math.Min(minColumn, column);
            maxColumn = Math.Max(maxColumn, column);
        }
        return maxRow >= 0;
    }

    private static bool OutsideRegionIsEmpty(
        IReadOnlyList<ItemSlot> slots,
        int rowOffset,
        int columnOffset,
        int width,
        int height,
        int inputWidth)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.itemData == null)
                continue;
            int row = i / inputWidth;
            int column = i % inputWidth;
            bool inside = row >= rowOffset && row < rowOffset + height &&
                          column >= columnOffset && column < columnOffset + width;
            if (!inside)
                return false;
        }
        return true;
    }
}

public sealed class CraftingRecipeMatch
{
    public CraftingRecipeMatch(RuntimeRecipe recipe, bool mirrored, IReadOnlyList<CraftingConsumption> consumptions)
    {
        Recipe = recipe;
        Mirrored = mirrored;
        Consumptions = consumptions ?? Array.Empty<CraftingConsumption>();
    }

    public RuntimeRecipe Recipe { get; }
    public bool Mirrored { get; }
    public IReadOnlyList<CraftingConsumption> Consumptions { get; }
}

public readonly struct CraftingConsumption
{
    public CraftingConsumption(int slotIndex, float amount)
    {
        SlotIndex = slotIndex;
        Amount = amount;
    }

    public int SlotIndex { get; }
    public float Amount { get; }
}
