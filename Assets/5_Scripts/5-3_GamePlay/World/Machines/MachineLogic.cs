using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>机器的粗粒度领域逻辑；库存与玩法状态不依赖世界 Item 或面板存活。</summary>
public abstract class MachineLogic : IDisposable
{
    #region 领域生命周期
    protected readonly List<Inventory> inventories = new();
    public MachineEntity Entity { get; }
    public IReadOnlyList<Inventory> Inventories => inventories;
    public virtual GameObject PanelPrefab => null;
    public virtual float TickInterval => .5f;
    public virtual bool IsBurning => false;
    public virtual float Progress01 => 0f;
    public virtual string Status => string.Empty;
    public virtual string ActionLabel => string.Empty;
    public virtual bool CanAct => true;
    public event Action Changed;
    private bool disposed;

    protected MachineLogic(MachineEntity entity)
        => Entity = entity ?? throw new ArgumentNullException(nameof(entity));

    /// <summary>同一机器的库存只由这里推进一次，领域逻辑不得重复调用 Inventory.ModUpdate。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Advance(float seconds)
    {
        if (disposed || !GameNetwork.HasStateAuthority || !MachineDefinition.Positive(seconds)) return;
        foreach (Inventory inventory in inventories) inventory.ModUpdate(seconds);
        Tick(seconds);
    }

    public virtual void Tick(float seconds) { }
    public abstract void Capture();
    public virtual bool Execute(string operation, string argument, Player actor) => false;
    public virtual bool ApplyRemoteSnapshot(ItemData snapshot) => false;
    protected void NotifyRemoteChanged() => Changed?.Invoke();

    /// <summary>权威变化与表现变化分开发送，进度变化不重建机器贴图。</summary>
    protected void NotifyChanged(bool visual = false)
    {
        Changed?.Invoke();
        MachineWorld.StateChanged(Entity);
        if (visual) MachineWorld.NotifyVisualChanged(Entity);
    }

    protected Inventory Track(Inventory inventory)
    {
        inventory.MachineOwner = Entity;
        inventories.Add(inventory);
        inventory.Data.Event_OnDataChanged += OnInventoryChanged;
        return inventory;
    }

    protected virtual void OnInventoryChanged(ItemSlot slot) => NotifyChanged();

    public virtual void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (Inventory inventory in inventories)
        {
            inventory.Data.Event_OnDataChanged -= OnInventoryChanged;
            inventory.DefaultTarget_Inventory = null;
            inventory.SyncQuickTransferTarget(null);
            inventory.UnbindController();
            inventory.UnbindRuntimeDataEvents();
            inventory.itemSlot_UI.Clear();
            inventory.item = null;
            inventory.MachineOwner = null;
        }
        Changed = null;
    }
    #endregion
}

/// <summary>库存构造共用现有事务，不复制 UI、Item、输入锁或事件订阅。</summary>
public static class MachineInventory
{
    #region 数据库存
    public static Inventory_Data NewData(string name, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var slots = new List<ItemSlot>(count);
        for (int i = 0; i < count; i++) slots.Add(new ItemSlot(i) { SlotMaxVolume = 100f });
        return new Inventory_Data(slots, name);
    }

    public static Inventory Create(Inventory template, Inventory_Data saved = null, string name = "库存", int count = 1)
    {
        var inventory = new Inventory
        {
            Data = saved ?? (template?.Data == null ? NewData(name, count) : MachinePersistence.Clone(template.Data)),
            StorageMaxWeightKg = template?.StorageMaxWeightKg ?? 0f,
            StorageMaxVolumeCubicMeters = template?.StorageMaxVolumeCubicMeters ?? 0f,
            InventoryPanel_Prefab = template?.InventoryPanel_Prefab
        };
        while (inventory.Data.itemSlots.Count < count)
            inventory.Data.itemSlots.Add(new ItemSlot(inventory.Data.itemSlots.Count) { SlotMaxVolume = 100f });
        Rebase(inventory.Data);
        inventory.InitData();
        inventory.DefaultTarget_Inventory = null;
        return inventory;
    }

    /// <summary>保留库存及数据对象身份，只替换槽位内容；已打开的面板不因同步重新创建。</summary>
    public static void ApplySnapshot(Inventory inventory, Inventory_Data incoming)
    {
        if (inventory == null || incoming?.itemSlots == null || incoming.itemSlots.Count == 0 || incoming.itemSlots.Count > 100000)
            throw new InvalidOperationException("机器库存快照无效。");
        if (ReferenceEquals(inventory.Data, incoming)) return;
        // 先完整校验候选数据，失败不能清空正在使用的库存与 UI 绑定。
        Rebase(incoming);
        List<ItemSlot> current = inventory.Data.itemSlots;
        bool resized = current.Count != incoming.itemSlots.Count;
        if (resized) inventory.UnbindSlotDataEvents();
        // 拖拽事务持有 ItemSlot 身份，同格同步只更新内容，不能每次换掉整个槽对象。
        for (int i = 0; i < incoming.itemSlots.Count; i++)
        {
            ItemSlot source = incoming.itemSlots[i];
            if (i >= current.Count) current.Add(source);
            else if (current[i] == null) current[i] = source;
            else
            {
                current[i].itemData = source.itemData;
                current[i].CanAcceptTags = source.CanAcceptTags;
                current[i].SlotMaxVolume = source.SlotMaxVolume;
                current[i].Index = i;
            }
        }
        if (current.Count > incoming.itemSlots.Count)
            current.RemoveRange(incoming.itemSlots.Count, current.Count - incoming.itemSlots.Count);
        inventory.Data.IsDepositBlocked = incoming.IsDepositBlocked;
        inventory.Data.IsInjected = incoming.IsInjected;
        inventory.RefreshUI();
        if (resized && inventory.itemSlot_UI.Count > 0) inventory.SyncData();
        if (inventory.item is Player player) PlayerCarryCapacityUtility.RefreshOverweightSlowdown(player);
    }

    public static void Rebase(Inventory_Data data)
    {
        if (data?.itemSlots == null) throw new InvalidOperationException("机器库存缺少槽位数据。");
        for (int i = 0; i < data.itemSlots.Count; i++)
        {
            ItemSlot slot = data.itemSlots[i] ?? throw new InvalidOperationException("机器库存包含空槽位记录。");
            slot.Index = i;
            if (slot.itemData != null && GameRes.ExistingInstance != null)
                slot.itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, slot.itemData);
        }
    }

    public static bool IsBeingDragged(Inventory inventory)
    {
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            if (inventory.IsSlotBeingDragged(i)) return true;
        return false;
    }
    #endregion
}
