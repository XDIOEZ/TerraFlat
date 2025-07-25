using MemoryPack;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
public class Mod_PlantGrow : Module
{
    public Mod_PlantGrow_Data _data = new Mod_PlantGrow_Data();
    public override ModuleData _Data
    {
        get => _data;
        set => _data = value as Mod_PlantGrow_Data;
    }
  

    private void Update()
    {
        // δ����������Ϊ�ջ��Ѿ�����򲻼���ִ��
        if (!_data.isRunning || _data == null || _data.progress >= _data.progress_Max.Value)
        {
            // ����Ƿ���Ҫѭ��
            if (_data.isRunning && _data.isLoop && _data.progress >= _data.progress_Max.Value)
            {
                ResetModule();
            }
            return;
        }

        // ������������
        _data.progress += _data.productionSpeed.Value * Time.deltaTime;

        // ����Ƿ�ﵽ����������
        if (_data.nodeIndex < _data.specialProductionPoints.Count &&
            _data.progress > _data.specialProductionPoints[_data.nodeIndex].InvokeValue)
        {
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
            Item product = RunTimeItemManager.Instance.InstantiateItem(loot.lootName, transform.position, transform.rotation);

           // new ItemMaker().DropItem_cric(product, transform.position, 2);

            GetComponent<Mod_ItemMaker>().DropItem_Range(product, transform.position, 2, 1);
            // ���ʵ�����ɹ���������Ʒ����
            if (product != null)
            {
                product.Item_Data.Stack.Amount = loot.lootAmount;
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
      //  throw new System.NotImplementedException();
    }

    public override void Save()
    {
//throw new System.NotImplementedException();
    }
}


[System.Serializable]
[MemoryPackable]
public partial class Mod_PlantGrow_Data : ModuleData
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
    public int nodeIndex;

    // ��ʼ��Ĭ��ֵ
    public Mod_PlantGrow_Data()
    {
        isRunning = false;
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
}
