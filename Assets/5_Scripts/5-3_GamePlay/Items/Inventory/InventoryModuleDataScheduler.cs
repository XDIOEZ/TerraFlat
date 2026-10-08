using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>库存纯数据能力的活动集合，槽位和模块结构变化时才重新登记。</summary>
public sealed class InventoryModuleDataScheduler : IDisposable
{
    #region 活动集合与实例时钟

    private sealed class Entry
    {
        public ItemData ItemData;
        public ModuleData ModuleData;
        public ItemSlot Slot;
        public int SlotIndex;
        public RuntimeItemDefinition Definition;
        public RuntimeModuleDataRulePlan Plan;
    }

    private sealed class InstanceClock
    {
        public uint LastHost;
        public double LastStepTime;
        public bool Initialized;

        public float TakeDelta(uint host, float seconds, double now)
        {
            // 跨库存共享同一模块时只扣除已经由另一宿主结算的时间窗口。
            if (Initialized && LastHost != host && now >= LastStepTime)
                seconds = Mathf.Min(seconds, Mathf.Max(0f, (float)(now - LastStepTime)));
            if (seconds <= 0f)
                return 0f;
            LastHost = host;
            LastStepTime = now;
            Initialized = true;
            return seconds;
        }
    }

    private static uint nextHostId;
    private static readonly ConditionalWeakTable<ModuleData, InstanceClock> clocks = new();
    private static readonly ConditionalWeakTable<ModuleData, InstanceClock>.CreateValueCallback createClock = _ => new InstanceClock();
    private readonly uint hostId = ++nextHostId;
    private readonly Inventory owner;
    private readonly HashSet<ItemData> observedItems = new();
    private readonly HashSet<ModuleData> registeredModules = new();
    private Inventory_Data boundData;
    private Entry[] entries = Array.Empty<Entry>();
    private uint structureRevision;
    private uint ruleRevision;
    private GameRes resourceSource;
    private int resourceRevision;
    private uint generation;
    private bool dirty = true;
    private bool isStepping;

    public InventoryModuleDataScheduler(Inventory owner)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    #endregion

    #region 结构登记与资源更新

    public void Bind(Inventory_Data data)
    {
        ReleaseBindings();
        boundData = data;
        if (boundData != null)
            boundData.RuntimeStructureChanged += HandleInventoryStructureChanged;
        dirty = true;
        generation++;
    }

    public void Dispose()
    {
        ReleaseBindings();
        boundData = null;
        entries = Array.Empty<Entry>();
        dirty = true;
        generation++;
    }

    private void ReleaseBindings()
    {
        if (boundData != null)
            boundData.RuntimeStructureChanged -= HandleInventoryStructureChanged;
        foreach (ItemData itemData in observedItems)
            itemData.ModuleStructureChanged -= HandleModuleStructureChanged;
        observedItems.Clear();
    }

    private void HandleInventoryStructureChanged(Inventory_Data data) => dirty = true;
    private void HandleModuleStructureChanged(ItemData itemData) => dirty = true;

    internal bool IsCurrentHost(Inventory_Data data, uint expectedGeneration)
        => generation == expectedGeneration && ReferenceEquals(boundData, data) && ReferenceEquals(owner.Data, data);

    private void EnsureReady()
    {
        if (!ReferenceEquals(boundData, owner.Data))
            Bind(owner.Data);
        if (boundData == null)
            return;
        uint currentStructureRevision = boundData.ContentStructureRevision;
        GameRes currentResourceSource = GameRes.ExistingInstance;
        int currentResourceRevision = currentResourceSource != null ? currentResourceSource.ResourceReloadVersion : 0;
        uint currentRuleRevision = ModuleDataRuleRegistry.Revision;
        if (currentStructureRevision != structureRevision || currentRuleRevision != ruleRevision ||
            !ReferenceEquals(resourceSource, currentResourceSource) || currentResourceRevision != resourceRevision)
            dirty = true;
        if (dirty)
            Rebuild(currentResourceSource, currentResourceRevision, currentRuleRevision);
    }

    private void Rebuild(GameRes gameRes, int currentResourceRevision, uint currentRuleRevision)
    {
        foreach (ItemData itemData in observedItems)
            itemData.ModuleStructureChanged -= HandleModuleStructureChanged;
        observedItems.Clear();
        registeredModules.Clear();
        dirty = false;
        Inventory_Data data = boundData;
        uint currentGeneration = generation;
        using var entryScope = ListPool<Entry>.Get(out var updatedEntries);
        using var moduleScope = ListPool<ModuleData>.Get(out var moduleSnapshot);
        if (data?.itemSlots != null)
        {
            for (int index = 0; index < data.itemSlots.Count; index++)
            {
                if (generation != currentGeneration || !ReferenceEquals(owner.Data, data))
                    break;
                ItemSlot slot = data.itemSlots[index];
                ItemData itemData = slot?.itemData;
                if (itemData == null || !observedItems.Add(itemData))
                    continue;
                itemData.ModuleStructureChanged += HandleModuleStructureChanged;
                try
                {
                    // 只补新冷数据的配置，已有活跃模块引用和嵌套库存进度保持原位。
                    if (gameRes != null && itemData.SharedConfiguration == null)
                        ItemDefinitionRuntime.RebasePersistedData(gameRes, itemData);
                    else if (gameRes != null)
                        ItemDefinitionRuntime.RebaseNestedPersistedItems(gameRes, itemData, onlyColdData: true);
                    if (!ReferenceEquals(slot.itemData, itemData) || itemData.ModuleDataDic == null)
                        continue;
                    RuntimeItemDefinition definition = null;
                    if (gameRes != null)
                        gameRes.TryGetItemDefinition(itemData.IDName, out definition);
                    moduleSnapshot.Clear();
                    foreach (ModuleData moduleData in itemData.ModuleDataDic.Values)
                        moduleSnapshot.Add(moduleData);
                    foreach (ModuleData moduleData in moduleSnapshot)
                    {
                        if (!ReferenceEquals(slot.itemData, itemData))
                            break;
                        if (moduleData == null || !moduleData.Enabled ||
                            string.IsNullOrEmpty(moduleData.StableName) ||
                            !itemData.ModuleDataDic.TryGetValue(moduleData.StableName, out ModuleData current) ||
                            !ReferenceEquals(current, moduleData) || !registeredModules.Add(moduleData))
                            continue;
                        RuntimeModuleDataRulePlan plan;
                        try
                        {
                            if (!ModuleDataRuleRegistry.TryGetTickPlan(moduleData, definition, out plan))
                                continue;
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError($"[InventoryModuleDataScheduler] 能力 {moduleData.ModuleId} 的登记失败：{exception}");
                            continue;
                        }
                        updatedEntries.Add(new Entry
                        {
                            ItemData = itemData, ModuleData = moduleData, Slot = slot,
                            SlotIndex = index, Definition = definition, Plan = plan
                        });
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[InventoryModuleDataScheduler] 库存 {data.Name} 的物品 {itemData.IDName} 登记失败：{exception}");
                }
            }
        }
        if (generation != currentGeneration || !ReferenceEquals(owner.Data, data))
            return;
        entries = updatedEntries.ToArray();
        structureRevision = data?.ContentStructureRevision ?? 0;
        ruleRevision = currentRuleRevision;
        resourceSource = gameRes;
        resourceRevision = currentResourceRevision;
    }

    #endregion

    #region 纯数据规则步进

    public void Tick(float deltaTime)
    {
        if (isStepping || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;
        isStepping = true;
        try
        {
            EnsureReady();
            Inventory_Data data = boundData;
            Entry[] snapshot = entries;
            uint currentGeneration = generation;
            double now = Time.timeAsDouble;
            foreach (Entry entry in snapshot)
            {
                if (generation != currentGeneration || !ReferenceEquals(owner.Data, data))
                    break;
                if (data?.itemSlots == null || (uint)entry.SlotIndex >= (uint)data.itemSlots.Count ||
                    !ReferenceEquals(data.itemSlots[entry.SlotIndex], entry.Slot) ||
                    !ReferenceEquals(entry.Slot.itemData, entry.ItemData))
                {
                    dirty = true;
                    continue;
                }
                var context = new ModuleDataTickContext(entry.ModuleData, entry.ItemData, data,
                    entry.Slot, entry.SlotIndex, deltaTime, entry.Definition, this, currentGeneration);
                if (!ModuleDataRuleRegistry.IsCurrent(context))
                {
                    dirty = true;
                    continue;
                }
                float instanceDelta = clocks.GetValue(entry.ModuleData, createClock).TakeDelta(hostId, deltaTime, now);
                if (instanceDelta <= 0f)
                    continue;
                context = new ModuleDataTickContext(entry.ModuleData, entry.ItemData, data,
                    entry.Slot, entry.SlotIndex, instanceDelta, entry.Definition, this, currentGeneration);
                ModuleDataRuleRegistry.Step(context, entry.Plan);
            }
        }
        finally
        {
            isStepping = false;
        }
    }

    #endregion
}
