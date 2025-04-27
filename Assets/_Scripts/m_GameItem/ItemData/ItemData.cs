using MemoryPack;
using NUnit.Framework.Interfaces;
using Org.BouncyCastle.Asn1.Cmp;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UltEvents;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;


// 使用[System.Serializable]特性使该类可以被序列化，以便在Unity编辑器中显示和编辑
[MemoryPackUnion(0, typeof(WeaponData))]//武器数据
[MemoryPackUnion(1, typeof(ArmorData))]//护甲数据
[MemoryPackUnion(2, typeof(AmmoData))]//弹药数据
[MemoryPackUnion(3, typeof(Tree_Data))]//树数据
[MemoryPackUnion(4, typeof(Apple_Red_Data))]//红苹果数据
[MemoryPackUnion(5, typeof(PlayerData))]//玩家数据
[MemoryPackUnion(6, typeof(CoalData))]//煤炭数据
[MemoryPackUnion(7, typeof(Com_ItemData))]//通用物品数据
[MemoryPackUnion(8, typeof(PickaxeToolData))]//矿石镐工具数据
[MemoryPackUnion(9, typeof(WorkerData))]//工作者数据
[MemoryPackUnion(10, typeof(StorageData))]//仓库数据
[MemoryPackUnion(11, typeof(ColdWeaponData))]//仓库数据
[MemoryPackUnion(12, typeof(FoodData))]//食物数据
[MemoryPackUnion(13, typeof(AnimalData))]//动物数据
[MemoryPackUnion(14, typeof(TileMapData))]//动物数据


[MemoryPackable(SerializeLayout.Explicit)]

[System.Serializable]   
public  abstract partial class ItemData
{
    [Foldout("物品数据")]
    [MemoryPackOrder(0)]
    [Tooltip("物品名称")]
    public string Name;

    [Foldout("物品数据")]
    [MemoryPackOrder(1)]
    [Tooltip("物品ID")]
    public int ID;

    [Foldout("物品数据")]
    [MemoryPackOrder(2)]
    [Tooltip("物品描述")]
    public string Description = "什么都没有描述";

    [Foldout("物品数据")]
    [MemoryPackOrder(3)]
    [Tooltip("预制体路径")]
    public string PrefabPath = "";

    [Foldout("物品数据")]
    [MemoryPackOrder(5)]
    [Tooltip("物品耐久度")]
    public float Durability = 1;

    [Foldout("物品数据")]
    [MemoryPackOrder(7)]
    [Tooltip("物品标签")]
    public ItemTag ItemTags;

    [Foldout("物品数据")]
    [MemoryPackOrder(8)]
    [Tooltip("物品堆叠信息")]
    public ItemStack Stack;

    [Foldout("物品数据")]
    [MemoryPackOrder(12)]
    [Tooltip("物品缩放")]
    public ItemTransform _transform;

    [Foldout("物品数据")]
    [MemoryPackOrder(13)]
    [Tooltip("物品特殊数据")]
    public string ItemSpecialData;

    [Foldout("物品数据")]
    [MemoryPackOrder(14)]
    [Tooltip("全局唯一标识")]
    public int Guid;


    //重写ToString方法，用于在控制台输出物品信息
    public override string ToString()
    {
        string str =
            $"物品名称：{Name}\n" +
            $"物品ID：{ID}\n" +
            $"物品描述：{Description}\n" +
            $"物品体积：{Stack.Volume}\n" +
            $"物品耐久度：{Durability}\n" +
            $"是否可拾取：{Stack.CanBePickedUp}\n" +
            $"物品标签：{ItemTags}\n" +
            $"物品堆叠信息：{Stack}\n" +
            $"物品位置：{_transform.Position}\n" +
            $"物品旋转：{_transform.Rotation}\n" +
            $"物品缩放：{_transform.Scale}\n" +
            $"物品特殊数据：{ItemSpecialData}\n" +
            $"全局唯一标识：{Guid}";

        return str;
    }

    [Sirenix.OdinInspector.Button("同步数据")]
    public virtual int SyncData()
    {
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        if (string.IsNullOrWhiteSpace(Name))
        {
            Debug.LogWarning("物品名称为空，无法同步数据");
            return -1;
        }

        var excel = m_ExcelManager.Instance;

        int nameColumn = excel.FindColumn(0, "Name");
        int itemRow = excel.FindRow(nameColumn, Name);
        int itemL = -1;

        // ID
        itemL = excel.FindColumn(0, "ID");
        ID = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // 描述
        itemL = excel.FindColumn(0, "Description");
        Description = excel.GetCellValue(itemRow, itemL).ToString();

        // 耐久度
        itemL = excel.FindColumn(0, "Durability");
        Durability = Convert.ToSingle(excel.GetCellValue(itemRow, itemL));

        // 特殊数据
        itemL = excel.FindColumn(0, "ItemSpecialData");
        ItemSpecialData = excel.GetCellValue(itemRow, itemL).ToString();

        // 物品堆叠信息
        itemL = excel.FindColumn(0, "Volume");
        Stack.Volume = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // 是否可拾取
        itemL = excel.FindColumn(0, "CanBePickedUp");
        object canBePickedUpValue = excel.GetCellValue(itemRow, itemL);
        if (canBePickedUpValue is double || canBePickedUpValue is int)
            Stack.CanBePickedUp = Convert.ToInt32(canBePickedUpValue) != 0;
        else
            Stack.CanBePickedUp = Convert.ToBoolean(canBePickedUpValue);

        // 类型标签列表
        itemL = excel.FindColumn(0, "Item_TypeTag");
        string typeTagStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_TypeTag = excel.ParseStringList(typeTagStr);

        // 材质标签列表
        itemL = excel.FindColumn(0, "Item_Material");
        string materialStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_Material = excel.ParseStringList(materialStr);

        Debug.Log("同步数据成功！");
        return itemRow;
    }



    // 新版方法：通过Vector3参数设置变换数据（含可选参数）
    public void SetTransformValue(Vector3 position,Quaternion? rotation = null,Vector3? scale = null)
    {
        _transform.Position = position;

        // 使用空合并运算符保持原有值
        _transform.Rotation = rotation ?? _transform.Rotation;
        _transform.Scale = scale ?? _transform.Scale;
    }

}

[MemoryPackable]
[System.Serializable]
public partial class ItemTag
{
    public List<string> Item_TypeTag = new List<string> { "None", "None" };
    public List<string> Item_Material = new List<string> { "None" };
    public override string ToString()
    {
        string str =
            $"物品类型：{Item_TypeTag[0]}({Item_TypeTag[1]})," +
            $"物品材质：{Item_Material[0]}";
        return str;
    }
    
}

[System.Serializable]
[MemoryPackable]
public partial class ItemTransform
{
    [Tooltip("物品位置")]
    public Vector3 Position;

    [Tooltip("物品旋转")]
    public Quaternion Rotation;

    [Tooltip("物品缩放")]
    public Vector3 Scale;
}


[MemoryPackable]
[System.Serializable]
public partial class ItemStack
{
    [Tooltip("物品数量")]
    public float Amount = 1;//物体的数量

    [Tooltip("物品体积")]
    // 公共浮点型变量Volume，用于存储物品的体积或其他相关数值
    public float Volume = 1;

    [Tooltip("是否可拾取")]
    public bool CanBePickedUp = true;

    [MemoryPackIgnore]
    [Tooltip("当前总体积")]
    public float CurrentVolume
    {
        get
        {
            return Amount * Volume;
        }
    }


    public override string ToString()
    {
        return string.Format("物体数量:{0}", Amount);
    }

}






