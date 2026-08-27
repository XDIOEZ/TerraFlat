using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Mod_EquipmentSaveData
{
    public Inventory_Data EquipmentInventoryData;
    public List<List<EquipmentInstance>> EquipmentInstances;
}

/// <summary>
/// 装备系统模块 —— 独立继承 Module，统一管理装备栏 Inventory UI、交互面板与装备效果实例。
/// 取代原先由 Mod_Inventory + Module_Equipment 分散实现的双模块方案。
/// </summary>
public class Mod_Equipment : Module, IInventory, IInteractable, IInstanceUI
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }

    #endregion

    #region 模组参数
    public Inventory_Equipment EquipmentInventory;

    // 每个槽位对应的装备实例列表
    [SerializeReference]
    List<List<EquipmentInstance>> equipment_Instances = new();

    // 缓存每个槽位对应的装备模块存档数据
    [SerializeReference]
    List<Ex_ModData_MemoryPackable> equipment_ModuleData = new();

    List<ItemData> cached_ItemDatas = new();

    #endregion

    #region 生命周期

    public void OnValidate()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.Name = ModText.Equipment_Module;
    }

    public override void Awake()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        if (string.IsNullOrEmpty(ModSaveData.ID))
            ModSaveData.ID = ModText.Equipment_Module;
        base.Awake();
    }

    public override void Load()
    {
        LoadSaveDataFromModule();

        // 设置所有者
        EquipmentInventory.item = item;

        // 设置默认目标背包
        var handMod = item.itemMods.GetMod_ByID(ModText.Hand);
        EquipmentInventory.DefaultTarget_Inventory = handMod != null
            ? handMod.GetComponent<Mod_Inventory>().inventory
            : Inventory_Hand.PlayerHand;

        // 初始化数据与控制器绑定
        var ctrl = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        EquipmentInventory.InitData();
        EquipmentInventory.BindController(ctrl);

        // 订阅槽位变化事件，驱动装备效果更新
        EquipmentInventory.Data.Event_OnDataChanged_TwoSlots -= UpdateEquipment;
        EquipmentInventory.Data.Event_OnDataChanged_TwoSlots += UpdateEquipment;

        EnsureEquipmentListSize();
        RebuildEquipmentRuntimeStateAfterLoad();
        RefreshBagStorageSlots();

        BindOpenPanelTrigger();

        // 恢复面板状态
        if (EquipmentInventory.Data.PanelIsOpen)
        {
            EquipmentInventory.EnsurePanelCreated();
            EquipmentInventory.basePanel.Open();
        }
    }

    private void OnDestroy()
    {
        Unload();
    }

    public override void Unload()
    {
        if (EquipmentInventory?.Data != null)
            EquipmentInventory.Data.Event_OnDataChanged_TwoSlots -= UpdateEquipment;

        UnbindOpenPanelTrigger();
    }

    public override void Save()
    {
        // 保存面板状态与位置
        var panel = EquipmentInventory.basePanel;
        if (panel != null)
        {
            EquipmentInventory.Data.PanelIsOpen = panel.IsOpen();

            var rt = panel.Dragger != null
                ? panel.Dragger.GetComponent<RectTransform>()
                : panel.GetComponent<RectTransform>();

            if (rt != null)
            {
                var p = rt.anchoredPosition;
                EquipmentInventory.Data.PanelPosition = new Vector3(p.x, p.y, EquipmentInventory.Data.PanelPosition.z);
            }
        }

        // 保存装备实例（不在此处卸下，避免影响其它模块的保存时序）
        SaveAllEquipmentModuleData();

        var saveData = new Mod_EquipmentSaveData
        {
            EquipmentInventoryData = EquipmentInventory.Data,
            EquipmentInstances = equipment_Instances
        };
        ModSaveData.WriteData(saveData);

        EquipmentInventory.Save();
    }

    public override void ModUpdate(float deltaTime)
    {
        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
                equip.Update();
        }
    }

    #endregion

    #region 交互处理

    protected virtual void BindOpenPanelTrigger()
    {
        // 基类不再依赖 Mod_InteractReciver。
        // 交互由 IInteractable 对外暴露，等待外部调用 OnInteractStart / OnInteractCancel。
    }

    protected virtual void UnbindOpenPanelTrigger()
    {
        // 基类无输入或事件订阅需要解绑。
    }

    public void OnInteractStart(Item playerItem)
    {
        Interact_Start(playerItem);
    }

    public void OnInteractCancel(Item playerItem)
    {
        Interact_Stop(playerItem);
    }

    protected virtual void Interact_Start(Item playerItem)
    {
        EquipmentInventory.EnsurePanelCreated();
        EquipmentInventory.basePanel.Toggle();

        var handInv = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInv != null)
            EquipmentInventory.DefaultTarget_Inventory = handInv;
    }

    protected virtual void Interact_Stop(Item playerItem)
    {
        EquipmentInventory.basePanel?.Close();
        EquipmentInventory.DefaultTarget_Inventory = null;
    }

    public Inventory GetDefaultTargetInventory() => EquipmentInventory.DefaultTarget_Inventory;

    public void I_ShowPanel()
    {
        if (EquipmentInventory == null)
            throw new System.InvalidOperationException("[Mod_Equipment] EquipmentInventory 为空，无法打开面板");

        EquipmentInventory.EnsurePanelCreated();
        if (EquipmentInventory.basePanel == null)
            throw new System.InvalidOperationException("[Mod_Equipment] basePanel 为空，无法打开面板");

        EquipmentInventory.basePanel.Open();
    }

    public void I_ClosePanel()
    {
        if (EquipmentInventory == null)
            throw new System.InvalidOperationException("[Mod_Equipment] EquipmentInventory 为空，无法关闭面板");

        if (EquipmentInventory.basePanel != null)
            EquipmentInventory.basePanel.Close();
    }

    public void I_TogglePanel()
    {
        if (EquipmentInventory == null)
            throw new System.InvalidOperationException("[Mod_Equipment] EquipmentInventory 为空，无法切换面板");

        EquipmentInventory.SwitchUI();
    }

    #endregion

    #region 装备逻辑

    /// <summary>重新挂载所有已装备收纳袋，兼容背包模块晚于装备模块恢复存档的顺序。</summary>
    public void RefreshBagStorageSlots()
    {
        Inventory ownerInventory = ResolveOwnerBagInventory();
        if (ownerInventory == null)
            return;

        DetachBagStorageSlots(ownerInventory);
        foreach (EquipmentInstance_Bag bag in GetEquippedBagInstances())
            bag.AttachToPlayerInventory(ownerInventory);
    }

    /// <summary>保存玩家行囊前临时移除装备扩展槽，避免把草笼内容重复写入玩家行囊存档。</summary>
    public void PrepareBagStorageForOwnerSave()
    {
        Inventory ownerInventory = ResolveOwnerBagInventory();
        if (ownerInventory != null)
            DetachBagStorageSlots(ownerInventory);
    }

    /// <summary>玩家行囊存档完成后恢复运行时扩展槽。</summary>
    public void RestoreBagStorageAfterOwnerSave()
    {
        RefreshBagStorageSlots();
    }

    private void DetachBagStorageSlots(Inventory ownerInventory)
    {
        foreach (EquipmentInstance_Bag bag in GetEquippedBagInstances())
            bag.DetachFromPlayerInventory(ownerInventory);
    }

    private IEnumerable<EquipmentInstance_Bag> GetEquippedBagInstances()
    {
        foreach (List<EquipmentInstance> list in equipment_Instances)
        {
            if (list == null)
                continue;

            foreach (EquipmentInstance instance in list)
            {
                if (instance is EquipmentInstance_Bag bag)
                    yield return bag;
            }
        }
    }

    private Inventory ResolveOwnerBagInventory()
    {
        if (item == null)
            return null;

        Mod_Inventory bagModule = item.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagModule?.inventory != null)
            return bagModule.inventory;

        Mod_Inventory[] modules = item.GetComponentsInChildren<Mod_Inventory>(true);
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i]?.inventory != null)
                return modules[i].inventory;
        }

        return null;
    }

    void LoadSaveDataFromModule()
    {
        try
        {
            Mod_EquipmentSaveData saveData = null;
            ModSaveData.ReadData(ref saveData);

            if (saveData != null)
            {
                if (saveData.EquipmentInventoryData != null)
                    EquipmentInventory.Data = saveData.EquipmentInventoryData;

                if (saveData.EquipmentInstances != null)
                    equipment_Instances = saveData.EquipmentInstances;

                return;
            }
        }
        catch
        {
            // 兼容旧版：旧版仅保存装备实例列表
        }

        ModSaveData.ReadData(ref equipment_Instances);
    }

    void EnsureEquipmentListSize()
    {
        if (EquipmentInventory?.Data == null) return;

        int slotCount = EquipmentInventory.Data.itemSlots.Count;
        while (equipment_Instances.Count < slotCount)
            equipment_Instances.Add(new List<EquipmentInstance>());
        while (equipment_ModuleData.Count < slotCount)
        {
            equipment_ModuleData.Add(null);
            cached_ItemDatas.Add(null);
        }
    }

    void RebuildEquipmentRuntimeStateAfterLoad()
    {
        EnsureEquipmentListSize();

        int slotCount = EquipmentInventory.Data.itemSlots.Count;
        for (int i = 0; i < slotCount; i++)
        {
            var slot = EquipmentInventory.Data.itemSlots[i];
            if (slot == null)
            {
                equipment_ModuleData[i] = null;
                cached_ItemDatas[i] = null;
                equipment_Instances[i] ??= new List<EquipmentInstance>();
                continue;
            }

            var slotItemData = slot.itemData;
            cached_ItemDatas[i] = slotItemData;

            if (slotItemData == null)
            {
                equipment_ModuleData[i] = null;
                equipment_Instances[i] ??= new List<EquipmentInstance>();
                continue;
            }

            equipment_ModuleData[i] = slotItemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
            equipment_Instances[i] ??= new List<EquipmentInstance>();

            if (equipment_Instances[i].Count == 0 && equipment_ModuleData[i] != null)
            {
                List<EquipmentInstance> loadedList = new();
                equipment_ModuleData[i].ReadData(ref loadedList);
                equipment_Instances[i] = loadedList ?? new List<EquipmentInstance>();
            }

            foreach (var equipment in equipment_Instances[i])
                equipment.Equip(item);
        }
    }

    void SaveAllEquipmentModuleData()
    {
        EnsureEquipmentListSize();
        for (int i = 0; i < equipment_Instances.Count; i++)
        {
            if (cached_ItemDatas[i] == null)
                continue;

            equipment_Instances[i] ??= new List<EquipmentInstance>();

            SaveSlotEquipmentData(i, cached_ItemDatas[i]);
        }
    }

    // 槽位数据变化时触发：卸下旧装备、加载新装备
    void UpdateEquipment(ItemSlot LocalSlot, ItemSlot pairSlot)
    {
        EnsureEquipmentListSize();
        int index = LocalSlot.Index;

        if (index < 0 || index >= equipment_Instances.Count)
        {
            Debug.LogError($"[Mod_Equipment] UpdateEquipment 槽位索引越界，index={index}, slots={equipment_Instances.Count}");
            return;
        }

        var previousItemData = cached_ItemDatas[index];
        bool slotItemChanged = !ReferenceEquals(previousItemData, LocalSlot.itemData);

        // 槽位物品发生变化：先把旧物品对应实例卸下并写回旧物品自身的存储模块
        if (slotItemChanged)
        {
            if (equipment_Instances[index].Count > 0)
            {
                foreach (var equip in equipment_Instances[index])
                    equip.UnEquip(item);

                SaveSlotEquipmentData(index, previousItemData);

                // 处理“卸下到手部时产生了新 ItemData 引用”的情况：
                // 把同一件物品的装备实例数据同步写回当前配对槽位中的新引用，避免再次装备时丢数据。
                SaveSlotEquipmentDataToPairedItem(index, previousItemData, pairSlot);

                equipment_Instances[index].Clear();
            }

            equipment_ModuleData[index] = null;
        }

        // 槽位变空：卸下当前装备
        if (LocalSlot.itemData == null)
        {
            cached_ItemDatas[index] = null;
            return;
        }

        equipment_ModuleData[index] =
            LocalSlot.itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
        cached_ItemDatas[index] = LocalSlot.itemData;

        // 槽位有物品且还未生成实例：读取存档并装备
        if (equipment_ModuleData[index] != null && equipment_Instances[index].Count == 0)
        {
            List<EquipmentInstance> loadedList = new();
            equipment_ModuleData[index].ReadData(ref loadedList);

            foreach (var equipment in loadedList)
                equipment.Equip(item);

            equipment_Instances[index] = loadedList;
        }
    }

    void SaveSlotEquipmentDataToPairedItem(int index, ItemData previousItemData, ItemSlot pairSlot)
    {
        if (pairSlot == null || pairSlot.itemData == null || previousItemData == null)
            return;

        var pairedItemData = pairSlot.itemData;
        if (ReferenceEquals(pairedItemData, previousItemData))
            return;

        bool isSameLogicalItem = pairedItemData.Guid == previousItemData.Guid &&
                                 pairedItemData.IDName == previousItemData.IDName &&
                                 pairedItemData.ItemSpecialData == previousItemData.ItemSpecialData;

        if (!isSameLogicalItem)
            return;

        var modData = pairedItemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
        if (modData == null)
        {
            Debug.LogError($"[Mod_Equipment] 同步配对槽位装备数据失败：物品[{pairedItemData.IDName}]缺少模块[{ModText.Equipment_Store}]");
            return;
        }

        modData.WriteData(equipment_Instances[index]);
    }

    void SaveSlotEquipmentData(int index, ItemData itemData)
    {
        if (itemData == null)
            return;

        var modData = itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
        if (modData == null)
        {
            Debug.LogError($"[Mod_Equipment] 写入装备数据失败：物品[{itemData.IDName}]缺少模块[{ModText.Equipment_Store}]");
            return;
        }

        modData.WriteData(equipment_Instances[index]);
    }

    public void EquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Mod_Equipment.EquipAll: item 为空 (模块 {name})");
            return;
        }
        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
                equip.Equip(item);
        }
    }

    public void UnEquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Mod_Equipment.UnEquipAll: item 为空 (模块 {name})");
            return;
        }
        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
                equip.UnEquip(item);
        }
    }

    #endregion
}
