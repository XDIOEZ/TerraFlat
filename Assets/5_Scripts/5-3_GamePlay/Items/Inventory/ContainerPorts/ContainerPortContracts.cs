using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

#region 类型化容器端口
[Flags] public enum ContainerPortDirection { Input = 1, Output = 2, Both = 3 }
[Flags] public enum ContainerAccessKind { Manual = 1, Animal = 2, Machine = 4, Mod = 8, All = 15 }
public enum ContainerTransferFailure { None, InvalidRequest, NoAuthority, StaleReference, SameStorage, AccessDenied, OutOfRange, FilterRejected, Empty, Full, Changed, Busy, CommitFailed, UnsupportedAtomicity }

/// <summary>调用者身份与用途独立于端口配置，MOD 仍须经过相同距离和所有权规则。</summary>
public readonly struct ContainerTransferContext
{
    public readonly Item Caller;
    public readonly ContainerAccessKind Access;
    public readonly string Purpose;
    public readonly Vector2 Position;
    public readonly int SceneHandle;
    public ContainerTransferContext(Item caller, ContainerAccessKind access, string purpose = null)
    { Caller = caller; Access = access; Purpose = purpose ?? string.Empty; Position = caller != null ? (Vector2)caller.transform.position : Vector2.zero; SceneHandle = caller != null ? caller.gameObject.scene.handle : 0; }
    public ContainerTransferContext(Vector2 position, int sceneHandle, ContainerAccessKind access, string purpose)
    { Caller = null; Access = access; Purpose = purpose ?? string.Empty; Position = position; SceneHandle = sceneHandle; }
    public ContainerTransferContext(Item caller, Vector2 position, int sceneHandle, ContainerAccessKind access, string purpose)
    { Caller = caller; Access = access; Purpose = purpose ?? string.Empty; Position = position; SceneHandle = sceneHandle; }
}

/// <summary>稳定身份配合运行代际使对象池旧引用立即失效。</summary>
public readonly struct ContainerPortReference
{
    public readonly string OwnerId, PortId;
    public readonly long Generation;
    public ContainerPortReference(string ownerId, long generation, string portId)
    { OwnerId = ownerId; Generation = generation; PortId = portId; }
    public override string ToString() => $"{OwnerId}/{Generation}/{PortId}";
}

[Serializable]
public sealed class ContainerPortConfiguration
{
    public int Version = ContainerTransferService.ContractVersion;
    public string Id = "contents";
    public string Type = "core:inventory";
    public string InventoryId;
    public ContainerPortDirection Direction = ContainerPortDirection.Both;
    public ContainerAccessKind Access = ContainerAccessKind.Manual | ContainerAccessKind.Machine | ContainerAccessKind.Mod;
    public float Reach = 2f;
    public bool WholeBatch;
    public bool OwnerOnly;
    public string[] ItemIds = Array.Empty<string>();
    public string[] ItemTags = Array.Empty<string>();
    public string[] LiquidIds = Array.Empty<string>();
    public int[] SlotIndices = Array.Empty<int>();
    public bool AllowsItem(ItemData data)
    {
        if (data == null) return false;
        bool ids = ItemIds == null || ItemIds.Length == 0;
        if (!ids) foreach (string id in ItemIds) if (id == data.IDName) { ids = true; break; }
        bool tags = ItemTags == null || ItemTags.Length == 0;
        if (!tags && data.Tags != null) foreach (string tag in ItemTags) foreach (string actual in data.Tags)
            if (string.Equals(actual, tag, StringComparison.OrdinalIgnoreCase)) { tags = true; break; }
        return ids && tags;
    }
    public bool AllowsLiquid(string id)
    { if (LiquidIds == null || LiquidIds.Length == 0) return true; foreach (string filter in LiquidIds) if (string.Equals(filter, id, StringComparison.OrdinalIgnoreCase)) return true; return false; }
    public static bool HasTag(ItemData data, string tag)
    { if (data?.Tags == null) return false; foreach (string actual in data.Tags) if (string.Equals(actual, tag, StringComparison.OrdinalIgnoreCase)) return true; return false; }
}

public interface IContainerPortProvider { void CollectContainerPorts(List<IContainerPort> ports); }
public interface IContainerPort
{
    ContainerPortReference Reference { get; }
    ContainerPortConfiguration Configuration { get; }
    object StorageIdentity { get; }
    long StateVersion { get; }
    bool IsValid { get; }
    bool SupportsAtomicTransfer { get; }
    bool CanAccess(ContainerTransferContext context, out ContainerTransferFailure failure);
    object CaptureState();
    void RestoreState(object snapshot);
    void PublishState();
}
public interface IItemTransferPort : IContainerPort
{
    ItemData PeekItem(string itemId = null, string tag = null);
    int GetReceivableItems(ItemData item, int requested);
    bool ExtractItems(ItemData expected, int amount, out ItemData extracted);
    bool InsertItems(ItemData item, int amount);
}
public readonly struct LiquidTransferBatch
{
    public readonly string LiquidId;
    public readonly float Servings, TemperatureCelsius;
    public LiquidTransferBatch(string liquidId, float servings, float temperatureCelsius)
    { LiquidId = liquidId; Servings = servings; TemperatureCelsius = temperatureCelsius; }
}
public interface ILiquidTransferPort : IContainerPort
{
    bool PeekLiquid(out LiquidTransferBatch batch);
    bool ReserveLiquid(out LiquidTransferBatch batch);
    float GetReceivableServings(string liquidId, float requested);
    bool ExtractLiquid(LiquidTransferBatch batch, float servings);
    bool InsertLiquid(LiquidTransferBatch batch, float servings);
}
public readonly struct ContainerTransferResult
{
    public readonly ContainerTransferFailure Failure;
    public readonly string ContentId;
    public readonly int Items;
    public readonly float LiquidServings;
    public readonly string Unit;
    public bool Success => Failure == ContainerTransferFailure.None && (Items > 0 || LiquidServings > 0f);
    public ContainerTransferResult(ContainerTransferFailure failure, string id = null, int items = 0, float servings = 0f, string unit = null)
    { Failure = failure; ContentId = id ?? string.Empty; Items = items; LiquidServings = servings; Unit = unit ?? (servings > 0f ? "servings" : "items"); }
}

/// <summary>原生适配器统一保存生命周期守卫，工厂只能提供能快照回滚的端口。</summary>
public abstract class ContainerPortBase : IContainerPort
{
    private static long nextGeneration;
    private readonly Func<bool> valid;
    private readonly long registryGeneration;
    protected readonly Item Owner;
    protected readonly Vector2 Position;
    protected readonly int SceneHandle;
    public ContainerPortReference Reference { get; }
    public ContainerPortConfiguration Configuration { get; }
    public abstract object StorageIdentity { get; }
    public abstract long StateVersion { get; }
    public bool IsValid => registryGeneration == ContainerPortFactoryRegistry.Generation && (valid == null || valid());
    public virtual bool SupportsAtomicTransfer => true;
    public ContainerPortBase(string ownerId, string portId, ContainerPortConfiguration configuration,
        Func<bool> valid, Item owner = null, Vector2 position = default, int sceneHandle = 0)
    {
        Reference = new ContainerPortReference(ownerId, Interlocked.Increment(ref nextGeneration), portId);
        Configuration = configuration ?? new ContainerPortConfiguration { Id = portId };
        registryGeneration = ContainerPortFactoryRegistry.Generation;
        this.valid = valid; Owner = owner; Position = position; SceneHandle = sceneHandle;
    }
    public virtual bool CanAccess(ContainerTransferContext context, out ContainerTransferFailure failure)
    {
        failure = ContainerTransferFailure.None;
        if (!IsValid) { failure = ContainerTransferFailure.StaleReference; return false; }
        if ((Configuration.Access & context.Access) != context.Access || context.Access == 0)
        { failure = ContainerTransferFailure.AccessDenied; return false; }
        Item effectiveOwner = Owner != null && Owner.Owner != null ? Owner.Owner : Owner;
        if (Configuration.OwnerOnly && effectiveOwner != context.Caller)
        { failure = ContainerTransferFailure.AccessDenied; return false; }
        Vector2 position = effectiveOwner != null ? (Vector2)effectiveOwner.transform.position : Position;
        int scene = effectiveOwner != null ? effectiveOwner.gameObject.scene.handle : SceneHandle;
        if (scene != context.SceneHandle || WorldTopologyRuntime.SqrDistance(position, context.Position) > Configuration.Reach * Configuration.Reach)
        { failure = ContainerTransferFailure.OutOfRange; return false; }
        return true;
    }
    public abstract object CaptureState();
    public abstract void RestoreState(object snapshot);
    public abstract void PublishState();
}
#endregion
