using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Production : Module
{
    [System.Serializable]
    [MemoryPackable]
    public partial class ItemProductionData
    {
        [MemoryPackIgnore]
        public GameObject itemPrefab;

        public string itemName;
        public int itemCount;

        [Tooltip("生成所需时间")]
        public float MaxProductionTime;

        [Tooltip("当前累计生产时间")]
        public float ProductionTime;

        [Tooltip("最大生产次数，-1 表示无限循环")]
        public int MaxProductionCount;

        [Tooltip("当前已生产次数")]
        public int CurrentProductionCount;
    }

    public List<ItemProductionData> ProductionList = new List<ItemProductionData>();
    public Ex_ModData_MemoryPackable _ModDataMemoryPackable;
    public Mod_Grow growModule;
    public float ProductionSpeed = 1f; // 生产速度倍率

    public override ModuleData _Data
    {
        get => _ModDataMemoryPackable;
        set => _ModDataMemoryPackable = (Ex_ModData_MemoryPackable)value;
    }

    private void OnValidate()
    {
        foreach (var data in ProductionList)
        {
            if (data.itemPrefab == null)
            {
                Debug.LogError("❌ ItemPrefab is null in ItemProductionData!");
                continue;
            }

            if (string.IsNullOrEmpty(data.itemName))
                data.itemName = data.itemPrefab.GetComponent<Item>().itemData.IDName;
        }
    }

    public override void Load()
    {
        _ModDataMemoryPackable.ReadData(ref ProductionList);

        if (item.itemMods.ContainsKey_ID(ModText.Grow))
            growModule = item.itemMods.GetMod_ByID(ModText.Grow) as Mod_Grow;

        Debug.Log($"[Load] 读取完成，当前生长状态 = {growModule?.Data.growState}");
    }

    public override void Save()
    {
        _ModDataMemoryPackable.WriteData(ProductionList);
    }

    public override void Action(float deltaTime)
    {
        if (growModule == null)
        {
            Debug.LogWarning("⚠️ growModule 未绑定，无法生产。");
            return;
        }

        // 只有成熟状态才能生产
        if (growModule.Data.growState != Mod_Grow.GrowState.成熟)
        {
            // Debug.Log("未成熟，不生产。");
            return;
        }

        foreach (var data in ProductionList)
        {
            if (data.itemPrefab == null) continue;

            // 判断是否达到生产上限
            if (data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
                continue;

            // 累加生产时间
            data.ProductionTime += deltaTime * ProductionSpeed;

            // 使用 while 确保不会漏算
            while (data.ProductionTime >= data.MaxProductionTime)
            {
                ProduceItem(data);
                data.ProductionTime -= data.MaxProductionTime;

                if (data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
                    break;
            }
        }
    }

    private void ProduceItem(ItemProductionData data)
    {
        if (string.IsNullOrEmpty(data.itemName))
            data.itemName = data.itemPrefab.GetComponent<Item>().itemData.IDName;

        Item item = ItemMgr.Instance.InstantiateItem(data.itemName);
        item.itemData.Stack.Amount = data.itemCount;

        data.CurrentProductionCount++;

        Debug.Log($"[生产完成] {data.itemName} ×{data.itemCount}, 已生产次数={data.CurrentProductionCount}");
    }
}
