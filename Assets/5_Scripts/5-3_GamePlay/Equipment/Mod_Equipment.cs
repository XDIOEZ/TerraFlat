using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 装备系统模块 —— 独立继承 Module，统一管理装备栏 Inventory UI、交互面板与装备效果实例。
/// 取代原先由 Mod_Inventory + Module_Equipment 分散实现的双模块方案。
/// </summary>
public class Mod_Equipment : Module, IInventory, IInteractable
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

    public void OnValidate() => _Data.Name = ModText.Equipment_Module;

    public override void Awake()
    {
        if (string.IsNullOrEmpty(_Data.ID))
            _Data.ID = ModText.Equipment_Module;
    }

    public override void Load()
    {
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
        EquipmentInventory.Data.Event_OnDataChanged_TwoSlots += UpdateEquipment;

        // 还原装备实例存档
        ModSaveData.ReadData(ref equipment_Instances);
        EnsureEquipmentListSize();

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

        // 保存装备实例并卸下
        SaveAllEquipmentModuleData();
        UnEquipAll();
        ModSaveData.WriteData(equipment_Instances);

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

    #endregion

    #region 装备逻辑

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

    void SaveAllEquipmentModuleData()
    {
        EnsureEquipmentListSize();
        // 各槽位数据已在 UpdateEquipment 时同步写回 ItemData，此处预留扩展点
    }

    // 槽位数据变化时触发：卸下旧装备、加载新装备
    void UpdateEquipment(ItemSlot LocalSlot, ItemSlot InputSlot)
    {
        EnsureEquipmentListSize();
        int index = LocalSlot.Index;

        // 槽位变空：卸下当前装备
        if (LocalSlot.itemData == null
            && equipment_ModuleData[index] != null
            && equipment_Instances[index].Count > 0)
        {
            foreach (var equip in equipment_Instances[index])
                equip.UnEquip(item);

            SaveSlotEquipmentData(index, InputSlot);
            equipment_Instances[index].Clear();
            equipment_ModuleData[index] = null;
            return;
        }

        if (equipment_ModuleData[index] == null)
            equipment_ModuleData[index] = new Ex_ModData_MemoryPackable();

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

    void SaveSlotEquipmentData(int index, ItemSlot Slot)
    {
        var modData = Slot.itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
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
