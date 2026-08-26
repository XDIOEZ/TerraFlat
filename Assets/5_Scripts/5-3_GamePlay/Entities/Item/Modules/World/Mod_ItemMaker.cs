using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Production : Module, IEnvironmentAdjustable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.5f;

    #region 数据结构

    [System.Serializable]
    [MemoryPackable]
    public partial class ItemProductionData
    {
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

        [Tooltip("将产出写入同一物品的库存接收模块，而不是直接生成世界物品")]
        public bool StoreInModule;

        [Tooltip("是否自动销毁自身item/在达到生产上限时销毁")]
        public bool DestroySelf = false;

        [Tooltip("实例化概率，取值范围 0~1")]
        [Range(0f, 1f)]
        public float SpawnProbability = 1f;

        [Header("随机初始化相关参数")]
        [Tooltip("生产时间随机范围")]
        public Vector2 Random_ProductionTime = new Vector2(0f, 1000f);
        [Tooltip("是否已经完成首次环境随机初始化")]
        public bool IsInitialized;

        public void RandomInitialize()
        {
            if (IsInitialized)
                return;

            ProductionTime = Random.Range(Random_ProductionTime.x, Random_ProductionTime.y);
            IsInitialized = true;
        }
    }

    #endregion

    #region 字段和属性

    public List<ItemProductionData> ProductionList = new List<ItemProductionData>();
    public Ex_ModData_MemoryPackable _ModDataMemoryPackable;
    public Mod_Grow growModule;
    public float ProductionSpeed = 1f; // 生产速度倍率
    [Tooltip("生产进度是否受作物生长难度倍率影响")]
    public bool UseCropGrowthMultiplier;

    private IProductionStockReceiver[] stockReceivers = System.Array.Empty<IProductionStockReceiver>();

    public override ModuleData _Data
    {
        get => _ModDataMemoryPackable;
        set => _ModDataMemoryPackable = (Ex_ModData_MemoryPackable)value;
    }

    #endregion

    #region 生命周期方法

    public override void Load()
    {
        _ModDataMemoryPackable.ReadData(ref ProductionList);

        stockReceivers = item.GetComponentsInChildren<IProductionStockReceiver>(true);
        foreach (ItemProductionData data in ProductionList)
        {
            if (data.StoreInModule && !HasStockReceiver(data.itemName))
            {
                throw new MissingComponentException(
                    $"[Mod_Production] 物品 {item.itemData.IDName} 的产出 {data.itemName} 缺少库存接收模块。");
            }
        }

        if (item.itemMods.ContainsKey_ID(ModText.Grow))
            growModule = item.itemMods.GetMod_ByID(ModText.Grow) as Mod_Grow;
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

            if (data.StoreInModule && !HasAvailableStockReceiver(data.itemName))
                continue;

            // 累加生产时间
            float difficultyMultiplier = UseCropGrowthMultiplier
                ? GameDifficultyService.Current.Production.CropGrowthMultiplier
                : 1f;
            data.ProductionTime += deltaTime * ProductionSpeed * difficultyMultiplier;

            // 使用 while 确保不会漏算
            while (data.ProductionTime >= data.MaxProductionTime)
            {
                if (!ProduceItem(data))
                    break;
                data.ProductionTime -= data.MaxProductionTime;

                if (data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
                    break;
            }
        }
    }

    #endregion

    #region 生产逻辑

    private bool ProduceItem(ItemProductionData data)
    {
        float spawnProbability = Mathf.Clamp01(data.SpawnProbability);
        if (Random.value > spawnProbability)
        {
            data.CurrentProductionCount++;

            if (data.DestroySelf && data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
            {
                StartCoroutine(DestroyAfterFrame());
            }

            return true;
        }

        int randomCount = Random.Range(data.itemCountMin, data.itemCountMax + 1);
        if (data.StoreInModule)
        {
            int accepted = StoreProducedItems(data.itemName, randomCount);
            if (accepted <= 0)
                return false;

            data.CurrentProductionCount++;
            DestroyOwnerWhenFinished(data);
            return true;
        }

        Item item = ItemMgr.Instance.InstantiateItem(data.itemName, transform.position);

        if (item != null)
        {
            item.Load();

            // 在范围内随机取值
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

        // 实现自动销毁功能
        DestroyOwnerWhenFinished(data);

        return item != null;
    }

    /// <summary>达到有限生产上限后延迟回收拥有者。</summary>
    private void DestroyOwnerWhenFinished(ItemProductionData data)
    {
        if (data.DestroySelf && data.MaxProductionCount != -1 &&
            data.CurrentProductionCount >= data.MaxProductionCount)
        {
            StartCoroutine(DestroyAfterFrame());
        }
    }

    /// <summary>判断同一物品上是否存在负责该产物的库存接收模块。</summary>
    private bool HasStockReceiver(string itemName)
    {
        foreach (IProductionStockReceiver receiver in stockReceivers)
        {
            if (receiver != null && receiver.AcceptsProduction(itemName))
                return true;
        }

        return false;
    }

    /// <summary>只有接收模块仍有容量时才推进生产计时。</summary>
    private bool HasAvailableStockReceiver(string itemName)
    {
        foreach (IProductionStockReceiver receiver in stockReceivers)
        {
            if (receiver != null && receiver.CanAcceptProduction(itemName))
                return true;
        }

        return false;
    }

    /// <summary>把产出交给同一物品的库存模块。</summary>
    private int StoreProducedItems(string itemName, int amount)
    {
        foreach (IProductionStockReceiver receiver in stockReceivers)
        {
            if (receiver != null && receiver.CanAcceptProduction(itemName))
                return receiver.AcceptProduction(itemName, amount);
        }

        return 0;
    }

    private System.Collections.IEnumerator DestroyAfterFrame()
    {
        yield return null; // 等待一帧
        if (item != null)
        {
            item.DestroySelf();
        }
    }

    #endregion

    #region 环境初始化

    public void AdjustByEnvironment(EnvironmentLayers layers, Vector2Int localPos)
    {
        if (layers == null || !layers.Contains(localPos.x, localPos.y))
            return;

        float normalizedTemp = Mathf.Clamp01(layers.Temperature[localPos.x, localPos.y]);
        float tempCelsius = layers.TemperatureCelsius[localPos.x, localPos.y];
        float precipitation = Mathf.Clamp01(layers.Precipitation[localPos.x, localPos.y]);

        float tempFactor = Mathf.Lerp(0.7f, 1.3f, normalizedTemp);
        float precipitationFactor = Mathf.Lerp(0.9f, 1.1f, precipitation);

        ProductionSpeed = Mathf.Clamp(tempFactor * precipitationFactor, 0.5f, 1.6f);

        // 对每个生产数据执行随机初始化
        foreach (var data in ProductionList)
        {
            data.RandomInitialize();
        }

        Debug.Log($"[Mod_Production] 环境初始化完成，温度01={normalizedTemp:F2}, 温度℃={tempCelsius:F1}, 降水={precipitation:F2}, 生产速度倍率={ProductionSpeed:F2}");
    }

    #endregion

    #region 验证和编辑器方法

    private void OnValidate()
    {
        foreach (var data in ProductionList)
        {
            data.SpawnProbability = Mathf.Clamp01(data.SpawnProbability);
            if (string.IsNullOrWhiteSpace(data.itemName))
                Debug.LogError("生产条目必须填写 JSON 物品 ID", this);

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

    #endregion
}
