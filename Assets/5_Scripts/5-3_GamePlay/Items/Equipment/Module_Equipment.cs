using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [已废弃] 请改用 <see cref="Mod_Equipment"/>，它整合了面板管理与交互功能。
/// 保留此文件仅用于平滑过渡。在 Inspector 中将组件替换为 Mod_Equipment 后可删除本文件。
/// </summary>
[System.Obsolete("请改用 Mod_Equipment，该类已将 Inventory UI 与装备逻辑整合为单一模块。")]
public class Module_Equipment : Module
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion
    #region 模组参数
    [SerializeReference]
    List<List<EquipmentInstance>> equipment_Instances = new();

    // 缓存每个槽位对应的装备模块存档数据（Ex_ModData_MemoryPackable）
    [SerializeReference]
    List<Ex_ModData_MemoryPackable> equipment_ModuleData = new();

    List<ItemData> cached_ItemDatas = new();


    [SerializeReference]
    public Inventory Equipment_inventory;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Equipment_Module;
        }
    }

    public override void Load()
    {

        Equipment_inventory.InitData();
        GameController GameController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        Equipment_inventory.BindController(GameController);

        // 当背包数据变化时，根据发生变化的槽位刷新装备
        Equipment_inventory.Data.Event_OnDataChanged_TwoSlots += UpdateEquipment;

        ModSaveData.ReadData(ref equipment_Instances);
        // 确保与背包槽位数量一致
        EnsureEquipmentListSize();

        Init();
    }

    // 确保 equipmentInstances 的长度至少与背包槽位数量一致
    void EnsureEquipmentListSize()
    {
        if (Equipment_inventory == null || Equipment_inventory == null || Equipment_inventory.Data == null)
            return;

        int slotCount = Equipment_inventory.Data.itemSlots.Count;
        while (equipment_Instances.Count < slotCount)
        {
            equipment_Instances.Add(new List<EquipmentInstance>());
        }

        while (equipment_ModuleData.Count < slotCount)
        {
            equipment_ModuleData.Add(null);
            cached_ItemDatas.Add(null);
        }
    }


    // 将所有槽位当前装备实例写回各自的模块数据
    void SaveAllEquipmentModuleData()
    {
        EnsureEquipmentListSize();
        for (int i = 0; i < equipment_Instances.Count; i++)
        {
            if (equipment_ModuleData[i] == null)
                continue;
        }
    }

    // 单个槽位刷新：由 OnDataChanged 调用
    void UpdateEquipment(ItemSlot LocalSlot, ItemSlot InputSlot)
    {
        EnsureEquipmentListSize();

        // 计算槽位索引
        int index = LocalSlot.Index;

        // 槽位为空或数量为 0：装备已经被卸下
        if (LocalSlot.itemData == null && equipment_ModuleData[index] != null && equipment_Instances[index].Count > 0)
        {
            if (equipment_Instances[index].Count == 0)
            {
                // 本来就没有装备，直接返回
                return;
            }

            // 卸下所有装备实例
            foreach (var equip in equipment_Instances[index])
            {
                equip.UnEquip(item);
            }
            // 本次刷新结束后，将最新数据写回对应模块
            SaveSlotEquipmentData(index, InputSlot);
            equipment_Instances[index].Clear();

            // 槽位没有物品了，对应模块数据引用也可以清空
            equipment_ModuleData[index] = null;

            return;
        }

        if (equipment_ModuleData[index] == null)
        {
            equipment_ModuleData[index] = new Ex_ModData_MemoryPackable();
        }

        equipment_ModuleData[index] = LocalSlot.itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
        cached_ItemDatas[index] = LocalSlot.itemData;

        // 槽位不为空：从模块数据中还原装备实例并安装
        if (equipment_ModuleData[index] != null && equipment_Instances[index].Count == 0)
        {
            // 注意：索引器不能直接作为 ref 传入，这里先用局部变量接收
            List<EquipmentInstance> loadedList = new List<EquipmentInstance>();
            equipment_ModuleData[index].ReadData(ref loadedList);

            foreach (var equipment in loadedList)
            {
                equipment.Equip(item);
            }

            // 用还原出的列表替换当前槽位的实例列表
            equipment_Instances[index] = loadedList;
        }
    }


    // 将指定槽位当前的装备实例列表写回对应的装备模块数据
    void SaveSlotEquipmentData(int index, ItemSlot Slot)
    {
        var modData = Slot.itemData.GetModuleData_Frist(ModText.Equipment_Store) as Ex_ModData_MemoryPackable;
        var list = equipment_Instances[index];//获取当前槽位的装备实例列表
        modData.WriteData(list);//写回数据
    }

    public void Init()
    {

    }

    public void EquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.EquipAll: item 为空，无法装备 (模块 {name})");
            return;
        }

        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
            {
                equip.Equip(item);
            }
        }
    }

    public void UnEquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.UnEquipAll: item 为空，无法卸下装备 (模块 {name})");
            return;
        }

        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
            {
                equip.UnEquip(item);
            }
        }
    }
    public override void ModUpdate(float deltaTime)
    {
        foreach (var list in equipment_Instances)
        {
            if (list == null) continue;
            foreach (var equip in list)
            {
                equip.Update();
            }
        }
    }
    public override void Save()
    {
        // 先把每个槽位当前的装备实例列表写回到各自的装备模块数据
        SaveAllEquipmentModuleData();

        UnEquipAll();

        // 仍然写入自身 ModSaveData，作为整体备份
        ModSaveData.WriteData(equipment_Instances);

    }
    public override void Act()
    {
        base.Act();
    }
    #endregion

}
