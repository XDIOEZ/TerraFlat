using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

/// <summary>晾架从机器空间索引读取燃烧热源，不再扫描 Collider 或实例化物品。</summary>
public class DryingRackLogic : MachineLogic
{
    #region 晾晒状态
    public SlotProcessingState State { get; }
    public Inventory Inventory { get; }
    public Meatrack VisualConfiguration { get; }
    public override GameObject PanelPrefab { get; }
    public IReadOnlyList<MeatrackDryingRule> Rules { get; }
    public int NearbyHeatSources { get; private set; }
    private readonly float radius;
    private readonly float heatBoost;
    private readonly List<string> sourceIds;
    private readonly List<string> sourceModules;

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
        Rules = config.Value("DryingRules", source.DryingRules) ?? new List<MeatrackDryingRule>();
        if (Rules.Count == 0) throw new InvalidOperationException("晾架未配置风干规则。");
        radius = config.Value("HeatSourceRadius", source.HeatSourceRadius);
        heatBoost = config.Value("HeatSourceSpeedBoostPerSource", source.HeatSourceSpeedBoostPerSource);
        sourceIds = config.Value("HeatSourceItemIds", source.HeatSourceItemIds) ?? new();
        sourceModules = config.Value("HeatSourceModuleIds", source.HeatSourceModuleIds) ?? new();
    }

    public MeatrackDryingRule GetRule(ItemData item)
    {
        if (item == null) return null;
        foreach (MeatrackDryingRule rule in Rules)
        {
            if (rule == null) continue;
            if (rule.InputItemIds != null)
                foreach (string id in rule.InputItemIds)
                    if (string.Equals(id, item.IDName, StringComparison.OrdinalIgnoreCase)) return rule;
            if (rule.InputTags != null && item.Tags != null && item.Tags.ContainsAnyTag(rule.InputTags)) return rule;
        }
        return null;
    }

    public float GetProgress(int index)
    {
        if ((uint)index >= (uint)Inventory.Data.itemSlots.Count) return 0f;
        var rule = GetRule(Inventory.Data.itemSlots[index].itemData);
        return rule == null ? 0f : Mathf.Clamp01(State.Elapsed[index] / Mathf.Max(.01f, rule.RequiredDryingSeconds));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public override void Tick(float seconds)
    {
        NearbyHeatSources = MachineWorld.CountBurningSources(Entity, radius, sourceIds, sourceModules);
        State.EnsureSlots(Inventory.Data.itemSlots.Count);
        bool changed = false;
        for (int i = 0; i < Inventory.Data.itemSlots.Count; i++)
        {
            ItemData item = Inventory.Data.itemSlots[i].itemData;
            State.Observe(i, item);
            MeatrackDryingRule rule = GetRule(item);
            if (rule == null || Inventory.IsSlotBeingDragged(i)) continue;
            State.Elapsed[i] += seconds * Mathf.Max(0f, rule.DrySpeedMultiplier) * (1f + NearbyHeatSources * Mathf.Max(0f, heatBoost));
            changed = true;
            if (State.Elapsed[i] < Mathf.Max(.01f, rule.RequiredDryingSeconds)) continue;
            if (State.PendingAmounts[i] < 0)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(item.Stack.Amount));
                int successes = 0;
                for (int n = 0; n < count; n++) if (UnityEngine.Random.value <= Mathf.Clamp01(rule.SuccessRate)) successes++;
                State.PendingAmounts[i] = successes;
                State.PendingItems[i] = rule.OutputItemId;
            }
            if (CraftingService.TransformSlot(Inventory, i, State.PendingItems[i], State.PendingAmounts[i], "machine.drying").Success)
                State.Reset(i);
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
