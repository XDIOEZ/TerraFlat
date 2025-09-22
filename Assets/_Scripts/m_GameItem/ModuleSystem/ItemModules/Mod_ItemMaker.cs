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
    [Tooltip("生产物品数量的最小值")]
    public int itemCountMin = 1;
    [Tooltip("生产物品数量的最大值")]
    public int itemCountMax = 1;

    [Tooltip("生成所需时间")]
    public float MaxProductionTime = 100f;

    [Tooltip("当前累计生产时间")]
    public float ProductionTime = 0;

    [Tooltip("最大生产次数，-1 表示无限循环")]
    public int MaxProductionCount = 10;

    [Tooltip("当前已生产次数")]
    public int CurrentProductionCount = 0;
        [Tooltip("是否抛出物品")]
    public bool ThrowItem = true;
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

    private void OnValiDate()
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

    public override void ModUpdate(float deltaTime)
    {
        if (!_Data.isRunning)
        {
            return;
        }

        if (growModule != null)
        {
            // 只有成熟状态才能生产
            if (growModule.Data.growState != Mod_Grow.GrowState.成熟)
            {
                // Debug.Log("未成熟，不生产。");
                return;
            }  
        }

        foreach (var data in ProductionList)
        {
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
    if (string.IsNullOrEmpty(data.itemName) && data.itemPrefab != null)
        data.itemName = data.itemPrefab.name;

    Item item = ItemMgr.Instance.InstantiateItem(data.itemName, transform.position);
    
    if (item != null)
    {
        item.Load();
        
        // 在范围内随机取值
        int randomCount = Random.Range(data.itemCountMin, data.itemCountMax + 1);
        item.itemData.Stack.Amount = randomCount;

        // 应用 ThrowItem 字段
        if (data.ThrowItem)
        {
            item.DropInRange();
        }

        data.CurrentProductionCount++;

        Debug.Log($"[生产完成] {data.itemName} ×{randomCount}, 已生产次数={data.CurrentProductionCount}");
    }
    else
    {
        Debug.LogError($"无法生产物品: {data.itemName}");
    }
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

        // 更新Prefab的名称作为itemName
        if (data.itemPrefab != null)
            data.itemName = data.itemPrefab.name;

        // 确保最小值不大于最大值
        if (data.itemCountMin > data.itemCountMax)
            data.itemCountMin = data.itemCountMax;
        
        if (data.itemCountMax < data.itemCountMin)
            data.itemCountMax = data.itemCountMin;
            
        // 确保至少生产1个物品
        if (data.itemCountMin < 1)
            data.itemCountMin = 1;
        if (data.itemCountMax < 1)
            data.itemCountMax = 1;
    }
}
}
