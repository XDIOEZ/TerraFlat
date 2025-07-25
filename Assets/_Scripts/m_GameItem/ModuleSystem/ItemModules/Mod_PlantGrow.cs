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
        // 未启动或数据为空或已经完成则不继续执行
        if (!_data.isRunning || _data == null || _data.progress >= _data.progress_Max.Value)
        {
            // 检查是否需要循环
            if (_data.isRunning && _data.isLoop && _data.progress >= _data.progress_Max.Value)
            {
                ResetModule();
            }
            return;
        }

        // 更新生产进度
        _data.progress += _data.productionSpeed.Value * Time.deltaTime;

        // 检查是否达到特殊生产点
        if (_data.nodeIndex < _data.specialProductionPoints.Count &&
            _data.progress > _data.specialProductionPoints[_data.nodeIndex].InvokeValue)
        {
            ProduceItem(_data.specialProductionPoints[_data.nodeIndex].loot);
        }
    }

  

    /// <summary>
    /// 重置模块（用于循环）
    /// </summary>
    private void ResetModule()
    {
        _data.progress = 0;
        _data.nodeIndex = 0;
     //   Debug.Log("模块已重置，循环开始！");
    }

    /// <summary>
    /// 生产物品（根据当前节点的掉落数据实例化物品）
    /// </summary>
    private void ProduceItem(LootData loot)
    {
        if (loot.lootName != "")
        {
            // 调用物品管理器实例化物品
            Item product = RunTimeItemManager.Instance.InstantiateItem(loot.lootName, transform.position, transform.rotation);

           // new ItemMaker().DropItem_cric(product, transform.position, 2);

            GetComponent<Mod_ItemMaker>().DropItem_Range(product, transform.position, 2, 1);
            // 如果实例化成功，设置物品数量
            if (product != null)
            {
                product.Item_Data.Stack.Amount = loot.lootAmount;
            }
            else
            {
                Debug.LogWarning($"无法实例化物品: {loot.lootName}");
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

    [Tooltip("是否循环执行")]
    public bool isLoop = false;

    [Tooltip("当前生产进度")]
    public float progress;

    [Tooltip("生产进度上限")]
    public GameValue_float progress_Max = new();

    [Tooltip("每秒生产速度")]
    public GameValue_float productionSpeed = new();

    [Tooltip("特殊生产点列表")]
    public List<Float_Loot_List> specialProductionPoints = new();

    [Tooltip("当前特殊生产点的索引")]
    public int nodeIndex;

    // 初始化默认值
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

    [Header("掉落数据")]
    public LootData loot;
}
