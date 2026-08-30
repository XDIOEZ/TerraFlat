using System;
using System.Collections.Generic;

/// <summary>
/// 制作失败阶段，供 UI、日志与联机层使用同一结果语义。
/// </summary>
public enum CraftingFailureReason
{
    None,
    InvalidInventory,
    RecipeNotFound,
    RecipeNotSupported,
    MissingMaterials,
    InvalidOutput,
    OutputSpaceInsufficient,
    InventoryChanged,
    CommitFailed
}

/// <summary>
/// 制作能力描述；普通合成仅限制可读取的输入槽，加热加工继续保留网格能力参数。
/// </summary>
public sealed class CraftingCapabilities
{
    public RecipeType RecipeType = RecipeType.Crafting;
    public int InputSlotLimit;
    public int MaxRecipeWidth;
    public int MaxRecipeHeight;
    public bool AllowCompactGrid;
    public bool AllowOutputIntoInput;
}

/// <summary>
/// 制作预览或提交结果。
/// </summary>
public sealed class CraftingResult
{
    private static readonly IReadOnlyList<ItemData> EmptyOutputs = Array.Empty<ItemData>();

    private CraftingResult(
        bool success,
        CraftingFailureReason failureReason,
        string message,
        RuntimeRecipe recipe,
        IReadOnlyList<ItemData> outputs)
    {
        Success = success;
        FailureReason = failureReason;
        Message = message ?? string.Empty;
        Recipe = recipe;
        Outputs = outputs ?? EmptyOutputs;
    }

    public bool Success { get; }
    public CraftingFailureReason FailureReason { get; }
    public string Message { get; }
    public RuntimeRecipe Recipe { get; }
    public IReadOnlyList<ItemData> Outputs { get; }
    public ItemData PrimaryOutput => Outputs.Count > 0 ? Outputs[0] : null;

    public static CraftingResult Succeeded(RuntimeRecipe recipe, IReadOnlyList<ItemData> outputs)
    {
        return new CraftingResult(true, CraftingFailureReason.None, string.Empty, recipe, outputs);
    }

    public static CraftingResult Failed(CraftingFailureReason reason, string message, RuntimeRecipe recipe = null)
    {
        return new CraftingResult(false, reason, message, recipe, EmptyOutputs);
    }
}
