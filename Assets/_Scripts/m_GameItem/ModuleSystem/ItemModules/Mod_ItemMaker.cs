using MemoryPack;

public partial class Mod_ItemMaker : Module
{
    [System.Serializable]
    [MemoryPackable]
    public partial class ItemMakerData
    {
        public string itemName;
        public int itemCount;
        // �������ʱ��
        public float MaxProductionTime;
        // ��ǰ����ʱ��
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
        // �ۼ�����ʱ��
        Data.ProductionTime += deltaTime;

        // ����ǰ����ʱ��ﵽ�򳬹��������ʱ�䣬������Ʒ�����ü�ʱ
        if (Data.ProductionTime >= Data.MaxProductionTime)
        {
            // ������Ʒ
            Item item = GameItemManager.Instance.InstantiateItem(Data.itemName);
            item.Item_Data.Stack.Amount = Data.itemCount;

            // ��ȥ����ʱ�䣨��ֹdeltaTime����©�㣩
            Data.ProductionTime -= Data.MaxProductionTime;

            // ����㲻�뱣������ʱ�䣬Ҳ����ֱ����Ϊ0��
            // Data.ProductionTime = 0f;
        }
    }

    public override void Save()
    {
        _ModDataMemoryPackable.WriteData(Data);
    }
}
