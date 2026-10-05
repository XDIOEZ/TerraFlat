using FastCloner.Code;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CompostBinSlotSnapshot
{
    public string ItemId = string.Empty; // 物品ID
    public float Amount = 1f; // 数量
    public string ItemSpecialData = string.Empty; // 特殊数据
    public float Durability = 1f; // 耐久
    public float MaxDurability = 1f; // 最大耐久
    public bool CanBePickedUp = true; // 是否可拾取
}

[Serializable]
public class CompostBinSaveState
{
    public string InventoryName = "堆肥桶"; // 库存名
    public List<CompostBinSlotSnapshot> Slots = new List<CompostBinSlotSnapshot>(); // 槽位快照
    public List<float> SlotElapsedSeconds = new List<float>(); // 槽位计时
}

public class Mod_CompostBin : Mod_MachineAuthoring
{

#region 基础参数

    public Ex_ModData ModSaveData = new Ex_ModData(); // 通用存档数据

    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = value as Ex_ModData ?? new Ex_ModData();
    }

    [ShowInInspector]
    public CompostBinSaveState Data = new CompostBinSaveState(); // 堆肥桶运行时/存档数据

    public Inventory CompostInventory = new Inventory(); // 堆肥桶库存
    public GameObject UI_Prefab; // 堆肥桶UI预制体
    public int SlotCount = 3; // 堆肥槽位数量
    public float CompostSeconds = 900f; // 单个槽位完成一次堆肥所需时间
    public string OutputItemId = string.Empty; // 化肥输出物品ID
    public List<string> OutputItemFallbackIds = new List<string> { "Fertilizer", "肥料", "Compost", "Compost_Fertilizer" }; // 输出候选ID
    public List<string> AcceptItemIds = new List<string>(); // 可堆肥物品ID
    public List<string> AcceptTags = new List<string>(); // 可堆肥Tag
    public List<string> AcceptKeywords = new List<string> 
    { "Leaf", "leaf", "Plant", "plant", "Rotten", "rotten", "腐", "肉", "叶" }; // 关键字兜底
    public bool EnableKeywordFallback = true; // 是否启用关键字兜底

#endregion

}
