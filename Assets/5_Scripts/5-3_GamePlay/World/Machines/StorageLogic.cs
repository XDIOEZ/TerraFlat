using System;
using System.Collections.Generic;
using MemoryPack;
using FlatWorld.Networking;
using UnityEngine;

[Serializable, MemoryPackable]
public partial class MachineStorageState
{
    #region 持久库存
    public List<Inventory_Data> Inventories = new();
    #endregion
}

/// <summary>箱子只维护库存，不分配加工器、扭矩状态或逐物品 GameObject。</summary>
public class StorageLogic : MachineLogic
{
    #region 储物设施
    public MachineStorageState State { get; }
    public override GameObject PanelPrefab => inventories.Count > 0 ? inventories[0].InventoryPanel_Prefab : null;

    public StorageLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_Inventory>()
            ?? throw new InvalidOperationException("储物设施缺少库存配置。");
        var authoring = (Mod_Inventory)config.Authoring;
        State = MachinePersistence.Read<MachineStorageState>(entity.Snapshot, "storage") ?? new MachineStorageState();
        // 当前生成/配置载荷中的初始库存同样有效，不仅恢复机器命名空间里的后续存档。
        Inventory_ModuleData initial = null;
        if (entity.Snapshot.ModuleDataDic?.TryGetValue(config.Declaration.StableName, out ModuleData module) == true)
            initial = module as Inventory_ModuleData;
        initial ??= authoring.Data;
        Inventoryinit initializer = null;
        if (!string.IsNullOrWhiteSpace(initial?.InventoryInitName))
            GameRes.Instance.InventoryInitGet(initial.InventoryInitName, out initializer);
        if (authoring.InventoryInstances == null || authoring.InventoryInstances.Count == 0)
            throw new InvalidOperationException("储物设施未配置库存。");
        for (int i = 0; i < authoring.InventoryInstances.Count; i++)
        {
            Inventory template = authoring.InventoryInstances[i];
            Inventory_Data saved = i < State.Inventories.Count ? State.Inventories[i] : null;
            bool firstCreation = saved == null;
            if (firstCreation && initial?.Data?.TryGetValue(Mod_Inventory.GetInventoryKey(template, i), out Inventory_Data seeded) == true)
                saved = MachinePersistence.Clone(seeded);
            Inventory inventory = Track(MachineInventory.Create(template, saved));
            if (i >= State.Inventories.Count) State.Inventories.Add(inventory.Data);
            else State.Inventories[i] = inventory.Data;
            if (firstCreation && GameNetwork.HasStateAuthority && initializer != null && !inventory.Data.IsInjected)
            {
                initializer.InjectRandomItemsToInventory(inventory);
                // 随机结果为空也是一次完成的生成，休眠或读档不能重新抽奖。
                inventory.Data.IsInjected = true;
            }
        }
    }

    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "storage", State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachinePersistence.Read<MachineStorageState>(snapshot, "storage");
        if (incoming?.Inventories?.Count != inventories.Count) return false;
        for (int i = 0; i < inventories.Count; i++) MachineInventory.ApplySnapshot(inventories[i], incoming.Inventories[i]);
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}
