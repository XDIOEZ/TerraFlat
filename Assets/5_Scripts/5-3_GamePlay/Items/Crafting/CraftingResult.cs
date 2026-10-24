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
    CommitFailed,
    ConditionsNotMet
}

/// <summary>
/// 制作能力描述；普通合成仅限制可读取的输入槽，加热加工继续保留网格能力参数。
/// </summary>
public sealed class CraftingCapabilities
{
    #region 制作能力与站点资格
    public RecipeType RecipeType = RecipeType.Crafting;
    public string StationId = string.Empty;
    public IReadOnlyList<string> CompatibleStationIds; // 同类制作入口可额外接受的工作站标识。
    public int InputSlotLimit;
    public int MaxRecipeWidth;
    public int MaxRecipeHeight;
    public bool AllowCompactGrid;
    public bool AllowOutputIntoInput;
    public bool ApplyOutputMultiplier = true; // 固定物料转换可关闭普通合成的规则增产。
    public bool RequireExplicitStation; // 专用设备只接收明确归属本站的配方。

    /// <summary>空标识对所有普通制作入口开放；额外标识仅由工作站显式声明。</summary>
    public bool SupportsStation(string requiredStation)
    {
        if (RequireExplicitStation && string.IsNullOrWhiteSpace(requiredStation)) return false;
        if (string.IsNullOrWhiteSpace(requiredStation) ||
            string.Equals(requiredStation, StationId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (CompatibleStationIds == null)
            return false;

        for (int index = 0; index < CompatibleStationIds.Count; index++)
            if (string.Equals(requiredStation, CompatibleStationIds[index], StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
    #endregion
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
