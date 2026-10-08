using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FlatWorld.Networking;

/// <summary>完整液体容器与冷库存载荷共享真实组分、每口预留和提交刷新。</summary>
public sealed class LiquidVesselTransferPort : ContainerPortBase, ILiquidTransferPort
{
    #region 液体适配
    private readonly ILiquidVessel vessel;
    public LiquidVesselTransferPort(ILiquidVessel vessel, ContainerPortConfiguration configuration = null, Func<bool> valid = null)
        : base(vessel.Item != null || vessel is ColdInventoryLiquidVessel ? $"item:{vessel.ItemData.Guid}" : $"machine:{vessel.Machine?.Id}",
            configuration?.Id ?? "liquids", configuration ?? new ContainerPortConfiguration { Id = "liquids", Type = "core:liquid_vessel" },
            valid ?? Guard(vessel), vessel.Item != null ? vessel.Item : (vessel as ColdInventoryLiquidVessel)?.Inventory.MachineOwner == null ? (vessel as ColdInventoryLiquidVessel)?.Owner : null,
            vessel.Machine != null ? (Vector2)vessel.Machine.Position : (vessel as ColdInventoryLiquidVessel)?.Inventory.MachineOwner != null ? (Vector2)((ColdInventoryLiquidVessel)vessel).Inventory.MachineOwner.Position : Vector2.zero,
            vessel.Machine != null || (vessel as ColdInventoryLiquidVessel)?.Inventory.MachineOwner != null ? SceneManager.GetSceneByName(MachineWorld.WorldKey).handle : 0)
    { this.vessel = vessel; }
    private static Func<bool> Guard(ILiquidVessel vessel)
    {
        if (vessel is ColdInventoryLiquidVessel cold) return () => cold.IsValid;
        if (vessel.Item != null)
        { Item item = vessel.Item; uint generation = item.RuntimeGeneration; ItemData data = vessel.ItemData;
          return () => item != null && item.IsInitialized && !item.DestructionHandled && item.RuntimeGeneration == generation && ReferenceEquals(item.itemData, data); }
        MachineEntity machine = vessel.Machine;
        return () => machine != null && MachineWorld.Contains(machine) && ReferenceEquals(machine.Logic, vessel);
    }
    public override object StorageIdentity => (object)vessel.ItemData ?? vessel.Data;
    public override long StateVersion => vessel.Data.Revision;
    public bool PeekLiquid(out LiquidTransferBatch batch)
    { batch = default; return IsValid && MixedLiquidContents.Peek(vessel.Data, Reference.PortId, out batch); }
    public bool ReserveLiquid(out LiquidTransferBatch batch)
    {
        batch = default;
        if (!IsValid || !GameNetwork.HasStateAuthority) return false;
        bool existing = vessel.Data.PendingOutputs?.ContainsKey(Reference.PortId) == true;
        bool result = MixedLiquidContents.Reserve(vessel.Data, Reference.PortId, out batch);
        if (result && !existing) vessel.CommitVessel();
        return result;
    }
    public float GetReceivableServings(string id, float requested)
        => Configuration.AllowsLiquid(id) && GameRes.ExistingInstance?.GetLiquidDefinition(id) != null
            ? Mathf.Min(requested, Mathf.Max(0f, vessel.Capacity - vessel.Data.Amount)) : 0f;
    public bool ExtractLiquid(LiquidTransferBatch batch, float servings)
        => IsValid && GameNetwork.HasStateAuthority && MixedLiquidContents.ConsumeBatch(vessel.Data, Reference.PortId, batch, servings);
    public bool InsertLiquid(LiquidTransferBatch batch, float servings)
    {
        if (!IsValid || !GameNetwork.HasStateAuthority || !(servings > 0f) || !float.IsFinite(servings) || GetReceivableServings(batch.LiquidId, servings) < servings) return false;
        MixedLiquidContents.Add(vessel.Data, batch.LiquidId, servings, batch.TemperatureCelsius); return true;
    }
    public override object CaptureState() => MixedLiquidContents.Clone(vessel.Data);
    public override void RestoreState(object snapshot) => MixedLiquidContents.Restore(vessel.Data, (LiquidContainerState)snapshot);
    public override void PublishState() => vessel.CommitVessel();
    #endregion
}
