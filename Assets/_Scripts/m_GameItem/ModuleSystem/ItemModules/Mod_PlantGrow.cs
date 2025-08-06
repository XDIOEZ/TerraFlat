using MemoryPack;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
public class Mod_PlantGrow : Module
{
    public Mod_PlantGrow_Data _data = new Mod_PlantGrow_Data();
    public Ex_ModData_MemoryPackable _memoryPackable = new Ex_ModData_MemoryPackable();
    public override ModuleData _Data
    {
        get => _memoryPackable;
        set => _memoryPackable = value as Ex_ModData_MemoryPackable;
    }


    public override void Action(float deltaTime)
    {
        // δ����������Ϊ�ջ��Ѿ�����򲻼���ִ��
        if (!_Data.isRunning || _data == null || _data.progress >= _data.progress_Max.Value)
        {
            // ����Ƿ���Ҫѭ��
            if (_Data.isRunning && _data.isLoop && _data.progress >= _data.progress_Max.Value)
            {
                ResetModule();
            }
            return;
        }

        // ������������
        _data.progress += _data.productionSpeed.Value * deltaTime;

        // ����Ƿ�ﵽ����������
        if (_data.nodeIndex < _data.specialProductionPoints.Count &&
            _data.progress > _data.specialProductionPoints[_data.nodeIndex].InvokeValue)
        {
            if (_data.specialProductionPoints[_data.nodeIndex].DestroySelf)
            {
                Destroy(item.gameObject);
            }

            ProduceItem(_data.specialProductionPoints[_data.nodeIndex].loot);
          
        }
    }

  

    /// <summary>
    /// ����ģ�飨����ѭ����
    /// </summary>
    private void ResetModule()
    {
        _data.progress = 0;
        _data.nodeIndex = 0;
     //   Debug.Log("ģ�������ã�ѭ����ʼ��");
    }

    /// <summary>
    /// ������Ʒ�����ݵ�ǰ�ڵ�ĵ�������ʵ������Ʒ��
    /// </summary>
    private void ProduceItem(LootData loot)
    {
        if (loot.lootName != "")
        {
            // ������Ʒ������ʵ������Ʒ
            Item product = GameItemManager.Instance.InstantiateItem(loot.lootName, transform.position, transform.rotation);

            // new ItemMaker().DropItem_cric(product, transform.position, 2);
            var dropComp = GetComponent<Mod_ItemDrop>();
            if (dropComp != null)
            {
                dropComp.DropItem_Range(product, transform.position, 2, 1);
            }

            // ���ʵ�����ɹ���������Ʒ����
            if (product != null)
            {
                product.itemData.Stack.Amount = loot.GetRandomLootAmount();
            }
            else
            {
                Debug.LogWarning($"�޷�ʵ������Ʒ: {loot.lootName}");
            }
        }

        _data.nodeIndex++;

        OnAction.Invoke(_data.nodeIndex);
    }

    public override void Load()
    {
        _memoryPackable.ReadData(ref _data);
      //  throw new System.NotImplementedException();
    }

    public override void Save()
    {
        _memoryPackable.WriteData(_data);
//throw new System.NotImplementedException();
    }
}


[System.Serializable]
[MemoryPackable]
public partial class Mod_PlantGrow_Data
{

    [Tooltip("�Ƿ�ѭ��ִ��")]
    public bool isLoop = false;

    [Tooltip("��ǰ��������")]
    public float progress;

    [Tooltip("������������")]
    public GameValue_float progress_Max = new();

    [Tooltip("ÿ�������ٶ�")]
    public GameValue_float productionSpeed = new();

    [Tooltip("�����������б�")]
    public List<Float_Loot_List> specialProductionPoints = new();

    [Tooltip("��ǰ���������������")]
    public int nodeIndex= 0;

    // ��ʼ��Ĭ��ֵ
    public Mod_PlantGrow_Data()
    {
        isLoop = false;
        progress = 0;
        nodeIndex = 0;
        specialProductionPoints = new List<Float_Loot_List>();
    }
}

[System.Serializable]
[MemoryPackable]
public partial class Float_Loot_List
{
    public string NodeName;
    public float InvokeValue;

    [Header("��������")]
    public LootData loot;

    public bool DestroySelf = false;
}
