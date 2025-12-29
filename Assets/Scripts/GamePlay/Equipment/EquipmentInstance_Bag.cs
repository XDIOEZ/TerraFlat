using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Bag : EquipmentInstance
{
    public string ID_BagPrefab = "Equipment_Bag";

    [Header("对象缓存")]
    [MemoryPackIgnore]
    public GameObject Cached_BagObject = null;

    [Button]
    public override void Equip(Item item = null)
    {
        if (item == null)
        {
            Debug.LogError("EquipmentInstance_Bag.Equip 调用时传入的 Item 为 null");
            return;
        }

        // 获取当前物品上的装备模块
        var equipModule = item.itemMods.GetMod_ByID<Module_Equipment>(ModText.Equipment_Module);
        if (equipModule == null)
        {
            Debug.LogError($"EquipmentInstance_Bag.Equip: 物品 {item.name} 上没有找到 Module_Equipment 模块");
            return;
        }

        // 实例化背包预制体
        Cached_BagObject = GameRes.Instance.InstantiatePrefab(ID_BagPrefab);
        if (Cached_BagObject == null)
        {
            Debug.LogError($"EquipmentInstance_Bag.Equip: 预制体 '{ID_BagPrefab}' 实例化失败");
            return;
        }

        // 设置为模块物体的子物体（保持本地 Transform 不变）
        Cached_BagObject.transform.SetParent(equipModule.transform, false);
    }

    public override void Update()
    {

    }

    [Button]
    public override void UnEquip(Item item = null)
    {
        //销毁
        if (Cached_BagObject != null)
        {
            GameObject.Destroy(Cached_BagObject);
            Cached_BagObject = null;
        }
    }

}
