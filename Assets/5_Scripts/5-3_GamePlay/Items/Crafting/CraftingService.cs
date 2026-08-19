using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

/// <summary>
/// 制作公共服务；所有入口通过同一预检与提交路径执行。
/// </summary>
public static class CraftingService
{
    private static readonly HashSet<Inventory> ActiveInventories = new HashSet<Inventory>();

    public static CraftingResult Preview(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingCapabilities capabilities)
    {
        return Prepare(inputInventory, outputInventory, capabilities, out _);
    }

    public static CraftingResult Craft(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingCapabilities capabilities,
        Player actor = null)
    {
        if (inputInventory == null || outputInventory == null)
            return CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "输入或输出库存为空");
        if (!ActiveInventories.Add(inputInventory))
            return CraftingResult.Failed(CraftingFailureReason.InventoryChanged, "该输入库存正在执行另一笔制作事务");

        try
        {
            CraftingResult prepared = Prepare(inputInventory, outputInventory, capabilities, out CraftingTransaction transaction);
            if (!prepared.Success)
                return prepared;

            if (!transaction.Commit(out CraftingResult commitFailure))
                return commitFailure;

            try
            {
                RecipeActionRunner.Execute(prepared.Recipe, inputInventory);
                transaction.Complete();
            }
            catch (Exception exception)
            {
                transaction.Rollback();
                Debug.LogException(exception);
                return CraftingResult.Failed(
                    CraftingFailureReason.CommitFailed,
                    $"制作动作执行失败，事务已回滚：{exception.Message}",
                    prepared.Recipe);
            }

            PublishSuccess(actor, prepared.Outputs);
            return prepared;
        }
        finally
        {
            ActiveInventories.Remove(inputInventory);
        }
    }

    private static CraftingResult Prepare(
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingCapabilities capabilities,
        out CraftingTransaction transaction)
    {
        transaction = null;
        if (inputInventory?.Data?.itemSlots == null || outputInventory?.Data?.itemSlots == null)
            return CraftingResult.Failed(CraftingFailureReason.InvalidInventory, "输入或输出库存未初始化");

        if (!CraftingRecipeMatcher.TryMatch(inputInventory, capabilities, out CraftingRecipeMatch match, out CraftingResult matchFailure))
            return matchFailure;

        if (!TryPrepareOutputs(match.Recipe, out List<ItemData> outputs, out CraftingResult outputFailure))
            return outputFailure;

        if (!CraftingTransaction.TryCreate(
                inputInventory,
                outputInventory,
                match,
                outputs,
                capabilities.AllowOutputIntoInput,
                out transaction,
                out CraftingResult transactionFailure))
        {
            return transactionFailure;
        }

        return CraftingResult.Succeeded(match.Recipe, outputs);
    }

    private static bool TryPrepareOutputs(
        RuntimeRecipe recipe,
        out List<ItemData> outputs,
        out CraftingResult failure)
    {
        outputs = new List<ItemData>();
        failure = null;
        if (recipe?.outputs?.results == null || recipe.outputs.results.Count == 0)
        {
            failure = CraftingResult.Failed(CraftingFailureReason.InvalidOutput, "配方没有产物", recipe);
            return false;
        }

        foreach (RuntimeRecipeResult output in recipe.outputs.results)
        {
            if (output == null || string.IsNullOrWhiteSpace(output.ItemName) || output.amount <= 0)
            {
                failure = CraftingResult.Failed(CraftingFailureReason.InvalidOutput, "配方包含无效产物", recipe);
                return false;
            }

            if (GameRes.Instance?.AllPrefabs == null ||
                !GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out GameObject prefab) ||
                prefab == null)
            {
                failure = CraftingResult.Failed(
                    CraftingFailureReason.InvalidOutput,
                    $"找不到产物 Prefab：{output.ItemName}",
                    recipe);
                return false;
            }

            ItemData itemData = GameRes.Instance.CreateItemData(output.ItemName);
            if (itemData?.Stack == null)
            {
                failure = CraftingResult.Failed(
                    CraftingFailureReason.InvalidOutput,
                    $"无法创建产物数据：{output.ItemName}",
                    recipe);
                return false;
            }

            itemData.Stack.Amount = GameDifficultyService.ScaleCount(
                output.amount,
                GameDifficultyService.Current.Production.CraftingOutputMultiplier,
                1);
            outputs.Add(itemData);
        }

        return true;
    }

    private static void PublishSuccess(Player actor, IReadOnlyList<ItemData> outputs)
    {
        for (int i = 0; i < outputs.Count; i++)
        {
            try
            {
                ItemData output = outputs[i];
                GameplayProgressEvents.PublishCraftSucceeded(
                    actor,
                    output?.IDName,
                    output?.Stack?.Amount ?? 1f);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}

/// <summary>合成预览失败诊断；只在有输入且失败原因变化时记录，避免空面板初始化刷屏。</summary>
public static class CraftingPreviewDiagnostics
{
    /// <summary>记录当前输入对应的预检失败原因，帮助定位配方、产物或库存阻塞。</summary>
    public static void ReportFailure(
        string source,
        Inventory inputInventory,
        CraftingResult result,
        ref string lastMessage)
    {
        if (result == null || result.Success)
        {
            lastMessage = string.Empty;
            return;
        }

        if (!HasInput(inputInventory))
            return;

        string message = string.IsNullOrWhiteSpace(result.Message)
            ? result.FailureReason.ToString()
            : result.Message;
        if (string.Equals(lastMessage, message, StringComparison.Ordinal))
            return;

        lastMessage = message;
        Debug.LogWarning($"[{source}] 配方检测失败：{message}");
    }

    /// <summary>判断输入库存是否真的放入了材料，过滤打开面板时的空预览。</summary>
    private static bool HasInput(Inventory inputInventory)
    {
        if (inputInventory?.Data?.itemSlots == null)
            return false;

        for (int i = 0; i < inputInventory.Data.itemSlots.Count; i++)
        {
            if (inputInventory.Data.itemSlots[i]?.itemData != null)
                return true;
        }

        return false;
    }
}
