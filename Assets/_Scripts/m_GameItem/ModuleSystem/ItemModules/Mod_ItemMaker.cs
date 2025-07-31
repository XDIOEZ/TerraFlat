using MemoryPack;

public partial class Mod_ItemMaker : Module
{
    [System.Serializable]
    [MemoryPackable]
    public partial class ItemMakerData
    {
        public string itemName;
        public int itemCount;
        // 最大生成时间
        public float MaxProductionTime;
        // 当前生产时间
        public float ProductionTime;
    }

    public ItemMakerData Data = new ItemMakerData();
    public Ex_ModData_MemoryPackable _ModDataMemoryPackable;

    public override ModuleData _Data
    {
        get => _ModDataMemoryPackable;
        set => _ModDataMemoryPackable = (Ex_ModData_MemoryPackable)value;
    }

    public override void Load()
    {
        _ModDataMemoryPackable.ReadData(ref Data);
    }

    public override void Action(float deltaTime)
    {
        // 累加生产时间
        Data.ProductionTime += deltaTime;

        // 当当前生产时间达到或超过最大生产时间，生产物品并重置计时
        if (Data.ProductionTime >= Data.MaxProductionTime)
        {
            // 生成物品
            Item item = GameItemManager.Instance.InstantiateItem(Data.itemName);
            item.Item_Data.Stack.Amount = Data.itemCount;

            // 减去生产时间（防止deltaTime过多漏算）
            Data.ProductionTime -= Data.MaxProductionTime;

            // 如果你不想保留多余时间，也可以直接置为0：
            // Data.ProductionTime = 0f;
        }
    }

    public override void Save()
    {
        _ModDataMemoryPackable.WriteData(Data);
    }
}
