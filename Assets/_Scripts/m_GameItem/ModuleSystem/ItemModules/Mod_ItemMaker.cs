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
        // �������ʱ��
        public float MaxProductionTime;
        // ��ǰ����ʱ��
        public float ProductionTime;
        // ������ɴ�����-1 ��ʾ����ѭ��
        public int MaxProductionCount;
        // ��ǰ���ɴ���
        public int CurrentProductionCount;
        public Mod_Grow.GrowState ActiveState = Mod_Grow.GrowState.����;
    }

    public List<ItemProductionData> ProductionList = new List<ItemProductionData>();
    public Ex_ModData_MemoryPackable _ModDataMemoryPackable;
    public Mod_Grow growModule;
    public float ProductionTime = 1f; // �����ٶ�

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

        growModule.Data.growState = Mod_Grow.GrowState.����;
    }

    public override void Save()
    {
        _ModDataMemoryPackable.WriteData(ProductionList);
    }

    public override void Action(float deltaTime)
    {
        foreach (var data in ProductionList)
        {
            // ���� ActiveState �� growModule ״̬ƥ��ʱ�Ž�������
            if (data.ActiveState == growModule.Data.growState)
            {
                // �����ǰ���ɴ����Ѵﵽ������������������Ϊ-1����ֹͣ����
                if (data.MaxProductionCount != -1 && data.CurrentProductionCount >= data.MaxProductionCount)
                {
                    continue; // ������ǰѭ��������������
                }

                // �ۼ�����ʱ��
                data.ProductionTime += deltaTime * ProductionTime; // ���������ٶȵ���ʱ��

                // ����ǰ����ʱ��ﵽ�򳬹��������ʱ�䣬������Ʒ�����ü�ʱ
                if (data.ProductionTime >= data.MaxProductionTime)
                {
                    // ������Ʒ����
                    if(data.itemName == "")
                    {
                        data.itemName = data.itemPrefab.GetComponent<Item>().itemData.IDName;
                    }
                    Item item = GameItemManager.Instance.InstantiateItem(data.itemName);
                    item.itemData.Stack.Amount = data.itemCount;

                    // ���ӵ�ǰ���ɴ���
                    data.CurrentProductionCount++;

                    // �����ǰ���ɴ���С��������ɴ�����������ѭ����MaxProductionCountΪ-1������������ʱ��
                    if (data.MaxProductionCount == -1 || data.CurrentProductionCount < data.MaxProductionCount)
                    {
                        data.ProductionTime = 0f; // ��������ʱ�䣬��������
                    }
                    else
                    {
                        // �ﵽ������ɴ����󣬲��ټ�������
                        data.ProductionTime -= data.MaxProductionTime; // ��ȥ����ʱ�䣨��ֹdeltaTime����©�㣩
                    }
                }
            }
        }
    }
}
