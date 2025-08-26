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
        // 最大生成时间
        public float MaxProductionTime;
        // 当前生产时间
        public float ProductionTime;
        // 最大生成次数，-1 表示无限循环
        public int MaxProductionCount;
        // 当前生成次数
        public int CurrentProductionCount;
        public Mod_Grow.GrowState ActiveState = Mod_Grow.GrowState.成熟;
    }

    public List<ItemProductionData> ProductionList = new List<ItemProductionData>();
    public Ex_ModData_MemoryPackable _ModDataMemoryPackable;
    public Mod_Grow growModule;
    public float ProductionTime = 1f; // 生产速度

    public override ModuleData _Data
    {
        get => _ModDataMemoryPackable;
        set => _ModDataMemoryPackable = (Ex_ModData_MemoryPackable)value;
    }

    private void OnValidate()
    {
        ProductionList.ForEach(data =>
        {
            if (data.itemPrefab == null)
            {
                Debug.LogError("ItemPrefab is null in ItemProductionData!");
            }
            else
            {
                data.itemName = data.itemPrefab.GetComponent<Item>().itemData.IDName;
            }

        });
    }

    public override void Load()
    {
        _ModDataMemoryPackable.ReadData(ref ProductionList);

        if (item.itemMods.ContainsKey_ID(ModText.Grow))
            growModule = item.itemMods.GetMod_ByID(ModText.Grow) as Mod_Grow;

        growModule.Data.growState = Mod_Grow.GrowState.成熟;
    }

    public override void Save()
    {
        _ModDataMemoryPackable.WriteData(ProductionList);
    }

    public override void Action(float deltaTime)
    {
        foreach (var data in ProductionList)
        {
            // 仅当 ActiveState 与 growModule 状态匹配时才进行生产
            if (data.ActiveState == growModule.Data.growState)
            {
                // 如果当前生成次数已达到最大次数（且最大次数不为-1），停止生产
                if (data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
                {
                    continue; // 跳过当前循环，不进行生产
                }

                // 累加生产时间
                data.ProductionTime += deltaTime * ProductionTime; // 根据生产速度调节时间

                // 当当前生产时间达到或超过最大生产时间，生产物品并重置计时
                if (data.ProductionTime >= data.MaxProductionTime)
                {
                    // 生成物品方法
                    if(data.itemName == "")
                    {
                        data.itemName = data.itemPrefab.GetComponent<Item>().itemData.IDName;
                    }
                    Item item = GameItemManager.Instance.InstantiateItem(data.itemName);
                    item.itemData.Stack.Amount = data.itemCount;

                    // 增加当前生成次数
                    data.CurrentProductionCount++;

                    // 如果当前生成次数小于最大生成次数或者无限循环（MaxProductionCount为-1），重置生产时间
                    if (data.MaxProductionCount == -1 || data.CurrentProductionCount < data.MaxProductionCount)
                    {
                        data.ProductionTime = 0f; // 重置生产时间，继续生成
                    }
                    else
                    {
                        // 达到最大生成次数后，不再继续生产
                        data.ProductionTime -= data.MaxProductionTime; // 减去生产时间（防止deltaTime过多漏算）
                    }
                }
            }
        }
    }
}
