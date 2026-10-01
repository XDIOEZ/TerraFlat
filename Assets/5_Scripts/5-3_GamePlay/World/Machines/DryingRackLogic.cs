using System;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>晾架从机器空间索引读取燃烧热源，不再扫描 Collider 或实例化物品。</summary>
public class DryingRackLogic : MachineLogic
{
    #region 晾晒状态
    public SlotProcessingState State { get; }
    public Inventory Inventory { get; }
    public Meatrack VisualConfiguration { get; }
    public override GameObject PanelPrefab { get; }
    private readonly float airExposureMultiplier;

    public DryingRackLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Meatrack>() ?? throw new InvalidOperationException("晾架缺少内容配置。");
        var source = (Meatrack)config.Authoring;
        VisualConfiguration = source;
        State = MachinePersistence.Read<SlotProcessingState>(entity.Snapshot, "drying") ?? new SlotProcessingState();
        Inventory = Track(MachineInventory.Create(source.RackInventory, State.Inventory, "晾肉架", Mathf.Max(1, config.Value("SlotCount", source.SlotCount))));
        State.Inventory = Inventory.Data;
        State.EnsureSlots(Inventory.Data.itemSlots.Count);
        PanelPrefab = source.InventoryPanelPrefab != null ? source.InventoryPanelPrefab : Inventory.InventoryPanel_Prefab;
        Inventory.InventoryPanel_Prefab = PanelPrefab;
        airExposureMultiplier = Mathf.Max(0f,
            config.Value("AirExposureMultiplier", source.AirExposureMultiplier));
    }

    public bool CanDry(ItemData item) => ItemMatterRuntime.CanMoistureTransform(item);

    public float GetProgress(int index)
    {
        if ((uint)index >= (uint)Inventory.Data.itemSlots.Count) return 0f;
        return ItemMatterRuntime.GetMoistureProgress01(Inventory.Data.itemSlots[index].itemData);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public override void Tick(float seconds)
    {
        State.EnsureSlots(Inventory.Data.itemSlots.Count);
        bool changed = false;
        float ambientTemperature = TemperatureMgr.DefaultAmbientTemperature;
        TemperatureMgr.Instance.TryGetAmbientTemperature(Entity.Position, out ambientTemperature);
        for (int i = 0; i < Inventory.Data.itemSlots.Count; i++)
        {
            ItemData item = Inventory.Data.itemSlots[i].itemData;
            State.Observe(i, item);
            if (item == null || !CanDry(item) || Inventory.IsSlotBeingDragged(i)) continue;
            if (ItemMatterRuntime.Advance(item, ambientTemperature, airExposureMultiplier, seconds))
            {
                Inventory.Data.NotifyItemStateChanged(item);
                changed = true;
            }
            if (ItemMatterRuntime.TryApplySolidTransition(Inventory, i, "matter.drying"))
            {
                State.Reset(i);
                changed = true;
            }
        }
        if (changed) NotifyChanged(true);
    }

    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "drying", State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        State.ApplyRemote(MachinePersistence.Read<SlotProcessingState>(snapshot, "drying"), Inventory);
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}
