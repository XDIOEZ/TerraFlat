using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;

/// <summary>所有调用者共享权威校验和两端快照，成功才发布保存通知。</summary>
public static class ContainerTransferService
{
    #region 发现与提交
    public const int ContractVersion = 1;
    private static bool submitting;
    public static event Action<ContainerTransferResult> Transferred;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<IContainerPort> Discover(Item item)
    {
        var result = new List<IContainerPort>();
        if (item == null || item.DestructionHandled || item.itemMods == null) return result;
        foreach (Module module in item.Mods.Values)
            if (module is IContainerPortProvider provider && module != null && module.IsRuntimeLoaded && module.Enabled && module.item == item)
                provider.CollectContainerPorts(result);
        return result;
    }
    public static IContainerPort Resolve(Item item, string portId)
    { foreach (IContainerPort port in Discover(item)) if (port.Reference.PortId == portId && port.IsValid) return port; return null; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ValidateRequest(IContainerPort source, IContainerPort target, ContainerTransferContext context, out ContainerTransferFailure failure, bool requireAuthority = true)
    {
        failure = ContainerTransferFailure.None;
        if (source == null || target == null) { failure = ContainerTransferFailure.InvalidRequest; return false; }
        if (requireAuthority && !GameNetwork.HasStateAuthority) { failure = ContainerTransferFailure.NoAuthority; return false; }
        if (submitting) { failure = ContainerTransferFailure.Busy; return false; }
        if (ReferenceEquals(source.StorageIdentity, target.StorageIdentity)) { failure = ContainerTransferFailure.SameStorage; return false; }
        if (!source.SupportsAtomicTransfer || !target.SupportsAtomicTransfer) { failure = ContainerTransferFailure.UnsupportedAtomicity; return false; }
        if ((source.Configuration.Direction & ContainerPortDirection.Output) == 0 || (target.Configuration.Direction & ContainerPortDirection.Input) == 0)
        { failure = ContainerTransferFailure.AccessDenied; return false; }
        return source.CanAccess(context, out failure) && target.CanAccess(context, out failure);
    }
    public static ContainerTransferResult PreviewItems(IItemTransferPort source, IItemTransferPort target,
        ContainerTransferContext context, int requested, string itemId = null, string tag = null)
    {
        if (!ValidateRequest(source, target, context, out var failure, false)) return new(failure);
        if (requested <= 0) return new(ContainerTransferFailure.InvalidRequest);
        ItemData data = source.PeekItem(itemId, tag);
        if (data == null) return new(ContainerTransferFailure.Empty);
        if (!source.Configuration.AllowsItem(data) || !target.Configuration.AllowsItem(data)) return new(ContainerTransferFailure.FilterRejected);
        int available = Math.Min(requested, (int)Math.Floor(data.Stack.Amount + .0001f));
        int amount = Math.Min(available, target.GetReceivableItems(data, available));
        if (amount <= 0 || (source.Configuration.WholeBatch || target.Configuration.WholeBatch) && amount < requested) return new(ContainerTransferFailure.Full);
        return new(ContainerTransferFailure.None, data.IDName, amount);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ContainerTransferResult TransferItems(IItemTransferPort source, IItemTransferPort target,
        ContainerTransferContext context, int requested, string itemId = null, string tag = null)
    {
        if (!ValidateRequest(source, target, context, out var failure)) return new(failure);
        var preview = PreviewItems(source, target, context, requested, itemId, tag);
        if (!preview.Success) return preview;
        ItemData data = source.PeekItem(itemId, tag);
        long aVersion = source.StateVersion, bVersion = target.StateVersion;
        object a = source.CaptureState(), b = target.CaptureState();
        submitting = true;
        bool committed = false;
        try
        {
            if (!source.IsValid || !target.IsValid || source.StateVersion != aVersion || target.StateVersion != bVersion) return new(ContainerTransferFailure.Changed);
            if (!source.ExtractItems(data, preview.Items, out ItemData extracted) || !target.InsertItems(extracted, preview.Items))
            { source.RestoreState(a); target.RestoreState(b); return new(ContainerTransferFailure.CommitFailed); }
            committed = true;
            PublishCommitted(source, target, preview);
            return preview;
        }
        catch (Exception)
        { if (!committed) { source.RestoreState(a); target.RestoreState(b); } throw; }
        finally { submitting = false; }
    }
    public static ContainerTransferResult PreviewLiquid(ILiquidTransferPort source, ILiquidTransferPort target,
        ContainerTransferContext context, float requestedServings)
    {
        if (!ValidateRequest(source, target, context, out var failure, false)) return new(failure, unit: "servings");
        if (!(requestedServings > 0f) || float.IsInfinity(requestedServings)) return new(ContainerTransferFailure.InvalidRequest, unit: "servings");
        if (!source.PeekLiquid(out LiquidTransferBatch batch)) return new(ContainerTransferFailure.Empty, unit: "servings");
        return PreviewBatch(source, target, batch, requestedServings);
    }
    private static ContainerTransferResult PreviewBatch(ILiquidTransferPort source, ILiquidTransferPort target, LiquidTransferBatch batch, float requested)
    {
        if (!source.Configuration.AllowsLiquid(batch.LiquidId) || !target.Configuration.AllowsLiquid(batch.LiquidId)) return new(ContainerTransferFailure.FilterRejected, batch.LiquidId, unit: "servings");
        float available = Math.Min(batch.Servings, requested);
        float amount = Math.Min(available, target.GetReceivableServings(batch.LiquidId, available));
        if (!(amount > 0f) || (source.Configuration.WholeBatch || target.Configuration.WholeBatch) && amount < requested) return new(ContainerTransferFailure.Full, batch.LiquidId, unit: "servings");
        return new(ContainerTransferFailure.None, batch.LiquidId, servings: amount);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ContainerTransferResult TransferLiquid(ILiquidTransferPort source, ILiquidTransferPort target,
        ContainerTransferContext context, float requestedServings = 1f)
    {
        if (!ValidateRequest(source, target, context, out var failure)) return new(failure, unit: "servings");
        if (!(requestedServings > 0f) || float.IsInfinity(requestedServings)) return new(ContainerTransferFailure.InvalidRequest, unit: "servings");
        submitting = true;
        object a = null, b = null;
        bool committed = false;
        try
        {
            // 预留期间同样禁止重入；拒收保留本口批次，其他出口不会重复使用这一份。
            if (!source.ReserveLiquid(out LiquidTransferBatch batch)) return new(ContainerTransferFailure.Empty, unit: "servings");
            var preview = PreviewBatch(source, target, batch, requestedServings);
            if (!preview.Success) return preview;
            long av = source.StateVersion, bv = target.StateVersion;
            a = source.CaptureState(); b = target.CaptureState();
            if (!source.IsValid || !target.IsValid || source.StateVersion != av || target.StateVersion != bv) return new(ContainerTransferFailure.Changed, unit: "servings");
            if (!source.ExtractLiquid(batch, preview.LiquidServings) || !target.InsertLiquid(batch, preview.LiquidServings))
            { source.RestoreState(a); target.RestoreState(b); return new(ContainerTransferFailure.CommitFailed, unit: "servings"); }
            committed = true; PublishCommitted(source, target, preview); return preview;
        }
        catch (Exception) { if (!committed && a != null && b != null) { source.RestoreState(a); target.RestoreState(b); } throw; }
        finally { submitting = false; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PublishResult(ContainerTransferResult result) => Transferred?.Invoke(result);
    private static void PublishCommitted(IContainerPort source, IContainerPort target, ContainerTransferResult result)
    {
        // 状态已同时提交；即使某个刷新回调报错，也保证另一端保存，不能把成功转移撤回成重复物品。
        try { source.PublishState(); }
        finally { try { target.PublishState(); } finally { PublishResult(result); } }
    }
    #endregion
}
