using System;
using System.Collections.Generic;
using System.Text;
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

/// <summary>合成预览失败诊断；按输入快照去重并输出配方线路上下文。</summary>
public static class CraftingPreviewDiagnostics
{
    /// <summary>记录当前输入对应的预检失败原因，帮助定位事件、目录、匹配或库存阻塞。</summary>
    public static void ReportFailure(
        string source,
        Inventory inputInventory,
        CraftingResult result,
        ref string lastMessage,
        CraftingCapabilities capabilities = null)
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
        string inputSnapshot = DescribeInventory(inputInventory, capabilities?.InputSlotLimit ?? 0);
        string diagnosticKey = $"{result.FailureReason}|{message}|{inputSnapshot}";
        if (string.Equals(lastMessage, diagnosticKey, StringComparison.Ordinal))
            return;

        lastMessage = diagnosticKey;
        string matcherContext;
        try
        {
            matcherContext = capabilities != null
                ? CraftingRecipeMatcher.BuildDiagnostic(inputInventory, capabilities)
                : "未提供制作能力，无法列出类型候选配方";
        }
        catch (Exception exception)
        {
            // Debug 诊断自身不得中断正常预览线路。
            matcherContext = $"生成匹配诊断时异常：{exception.GetType().Name} / {exception.Message}";
        }
        Debug.LogWarning(
            $"[{source}][CraftingDebug] 配方检测失败：{result.FailureReason} / {message}\n" +
            $"输入快照：{inputSnapshot}\n" +
            $"匹配上下文：{matcherContext}");
    }

    /// <summary>生成稳定的槽位快照，便于串联输入事件与匹配日志。</summary>
    public static string DescribeInventory(Inventory inventory, int slotLimit = 0)
    {
        if (inventory?.Data?.itemSlots == null)
            return "库存/Data/槽位为空";

        int count = slotLimit > 0
            ? Mathf.Min(slotLimit, inventory.Data.itemSlots.Count)
            : inventory.Data.itemSlots.Count;
        var builder = new StringBuilder();
        builder.Append(inventory.Data.Name).Append(" [");
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(", ");

            ItemData itemData = inventory.Data.itemSlots[i]?.itemData;
            builder.Append(i).Append(':');
            if (itemData == null)
            {
                builder.Append("空");
                continue;
            }

            builder.Append(string.IsNullOrWhiteSpace(itemData.IDName) ? "<空ID>" : itemData.IDName.Trim());
            builder.Append(" x").Append(itemData.Stack?.Amount ?? 0f);
        }

        builder.Append(']');
        if (count < inventory.Data.itemSlots.Count)
            builder.Append("（仅检查前 ").Append(count).Append("/").Append(inventory.Data.itemSlots.Count).Append(" 槽）");
        return builder.ToString();
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
