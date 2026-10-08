using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public sealed partial class ModItemApi
{
    #region Lua容器意图
    private bool HasCurrentContainerOwner => item != null && item.IsInitialized && !item.DestructionHandled && item.RuntimeGeneration == runtimeGeneration;
    private ContainerTransferContext ContainerContext => new(item != null && item.Owner != null ? item.Owner : item, ContainerAccessKind.Mod, "lua-container-intent");

    /// <summary>Lua 只得到可访问端口的 JSON 副本及代际句柄，不暴露可写库存或工厂。</summary>
    public string GetContainerPortsJson(int targetGuid = 0)
    {
        var rows = new List<object>();
        Item owner = HasCurrentContainerOwner ? targetGuid == 0 ? item : ItemMgr.Instance?.GetItemByGuid(targetGuid) : null;
        if (owner == null) return "[]";
        foreach (IContainerPort port in ContainerTransferService.Discover(owner))
        {
            if (!port.CanAccess(ContainerContext, out _)) continue;
            ItemData first = (port as IItemTransferPort)?.PeekItem();
            LiquidTransferBatch liquid = default;
            bool hasLiquid = port is ILiquidTransferPort fluid && fluid.PeekLiquid(out liquid);
            rows.Add(new
            {
                handle = port.Reference.ToString(), ownerGuid = owner.itemData.Guid,
                id = port.Reference.PortId, type = port.Configuration.Type,
                direction = port.Configuration.Direction.ToString(), unit = port is ILiquidTransferPort ? "servings" : "items",
                version = port.StateVersion,
                contractVersion = ContainerTransferService.ContractVersion, configurationVersion = port.Configuration.Version,
                firstItem = first == null ? null : new { id = first.IDName, guid = first.Guid, amount = first.Stack.Amount },
                nextLiquid = hasLiquid ? new { id = liquid.LiquidId, servings = liquid.Servings, temperatureCelsius = liquid.TemperatureCelsius } : null
            });
        }
        return JsonConvert.SerializeObject(rows);
    }

    public string GetLiquidCompositionJson()
    {
        var vessel = HasCurrentContainerOwner ? GetLiquidContainer() : null;
        return vessel == null ? "{}" : JsonConvert.SerializeObject(new
        { unit = "servings", capacity = vessel.Capacity, temperatureCelsius = vessel.Data.Temperature, components = vessel.Data.Composition });
    }

    public string PreviewItemTransfer(int targetGuid, string sourceHandle, string targetHandle, int amount, string itemId = null, string tag = null)
        => ItemTransferIntent(false, targetGuid, sourceHandle, targetHandle, amount, itemId, tag);
    public string RequestItemTransfer(int targetGuid, string sourceHandle, string targetHandle, int amount, string itemId = null, string tag = null)
        => ItemTransferIntent(true, targetGuid, sourceHandle, targetHandle, amount, itemId, tag);
    public string PreviewLiquidTransfer(int targetGuid, string sourceHandle, string targetHandle, float servings = 1f)
        => LiquidTransferIntent(false, targetGuid, sourceHandle, targetHandle, servings);
    public string RequestLiquidTransfer(int targetGuid, string sourceHandle, string targetHandle, float servings = 1f)
        => LiquidTransferIntent(true, targetGuid, sourceHandle, targetHandle, servings);

    private string ItemTransferIntent(bool commit, int targetGuid, string sourceHandle, string targetHandle, int amount, string itemId, string tag)
    {
        if (commit) ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("RequestItemTransfer");
        var source = FindContainerHandle(item, sourceHandle) as IItemTransferPort;
        var target = FindContainerHandle(ItemMgr.Instance?.GetItemByGuid(targetGuid), targetHandle) as IItemTransferPort;
        var result = source == null || target == null ? new ContainerTransferResult(ContainerTransferFailure.StaleReference) :
            commit ? ContainerTransferService.TransferItems(source, target, ContainerContext, amount, itemId, tag) :
                ContainerTransferService.PreviewItems(source, target, ContainerContext, amount, itemId, tag);
        return DescribeContainerResult(result);
    }
    private string LiquidTransferIntent(bool commit, int targetGuid, string sourceHandle, string targetHandle, float servings)
    {
        if (commit) ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("RequestLiquidTransfer");
        var source = FindContainerHandle(item, sourceHandle) as ILiquidTransferPort;
        var target = FindContainerHandle(ItemMgr.Instance?.GetItemByGuid(targetGuid), targetHandle) as ILiquidTransferPort;
        var result = source == null || target == null ? new ContainerTransferResult(ContainerTransferFailure.StaleReference, unit: "servings") :
            commit ? ContainerTransferService.TransferLiquid(source, target, ContainerContext, servings) :
                ContainerTransferService.PreviewLiquid(source, target, ContainerContext, servings);
        return DescribeContainerResult(result);
    }
    private IContainerPort FindContainerHandle(Item owner, string handle)
    {
        if (!HasCurrentContainerOwner || owner == null || string.IsNullOrWhiteSpace(handle)) return null;
        foreach (IContainerPort port in ContainerTransferService.Discover(owner))
            if (port.Reference.ToString() == handle && port.IsValid && port.CanAccess(ContainerContext, out _)) return port;
        return null;
    }
    private static string DescribeContainerResult(ContainerTransferResult result) => JsonConvert.SerializeObject(new
    { success = result.Success, reason = result.Failure.ToString(), contentId = result.ContentId, items = result.Items, servings = result.LiquidServings, unit = result.Unit });
    #endregion
}
