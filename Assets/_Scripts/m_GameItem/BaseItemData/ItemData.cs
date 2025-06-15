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
[MemoryPackUnion(0, typeof(Data_Creature))]//动物数据
[MemoryPackUnion(1, typeof(Data_Armor))]//护甲数据
[MemoryPackUnion(2, typeof(Data_Ammo))]//弹药数据
[MemoryPackUnion(3, typeof(Data_ColdWeapon))]//冷武器数据
[MemoryPackUnion(4, typeof(Data_GeneralItem))]//通用物品数据
[MemoryPackUnion(5, typeof(Data_Building))]//房屋数据
[MemoryPackUnion(6, typeof(Data_Player))]//玩家数据
[MemoryPackUnion(8, typeof(Data_TileMap))]//瓦片地图数据
[MemoryPackUnion(51, typeof(Data_Weapon))]//武器数据
[MemoryPackUnion(52, typeof(Data_Worker))]//工作者数据
[MemoryPackUnion(53, typeof(Data_Boundary))]//墙壁数据
[MemoryPackUnion(54, typeof(Data_Food))]//墙壁数据
[MemoryPackUnion(55, typeof(Data_Tile_Block))]//材料数据

[MemoryPackable]
[System.Serializable]   
public  abstract partial class ItemData
{
    [Tooltip("物品名称")]
    public string IDName;

    [Tooltip("物品名称")]
    public string GameName;

    [Tooltip("物品描述")]
    public string Description = "什么都没有描述";

    [Tooltip("物品耐久度")]
    public float Durability = 1;

    [Tooltip("物品标签")]
    public ItemTag ItemTags;

    [Tooltip("物品堆叠信息")]
    public ItemStack Stack;

    [Tooltip("物品缩放")]
    public ItemTransform _transform = new();

    [Tooltip("物品特殊数据")]
    public string ItemSpecialData;

    [Tooltip("全局唯一标识")]
    public int Guid;


    //重写ToString方法，用于在控制台输出物品信息
    public override string ToString()
    {
        string str =
            $"物品名称：{IDName}\n" +
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

    public virtual int SyncData()
    {
        //打开对应的Excel表格
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        var excel = m_ExcelManager.Instance;


        //查找对应的物品IDName
        int nameColumn = excel.FindColumn(0,ExcelIdentifyRow.IDName);


        int itemRow = excel.FindRow(nameColumn, IDName);
        int itemL = -1;

        if (itemRow == -1)
        {
            return -1;
        }

        itemL = excel.FindColumn(0, ExcelIdentifyRow.GameName);
        GameName = excel.GetCellValue(itemRow, itemL).ToString();
        // 描述
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Description);
        Description = excel.GetCellValue(itemRow, itemL).ToString();

        // 耐久度
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Durability);
        Durability = Convert.ToSingle(excel.GetCellValue(itemRow, itemL));

        // 特殊数据
        itemL = excel.FindColumn(0, ExcelIdentifyRow.ItemSpecialData);
        ItemSpecialData = excel.GetCellValue(itemRow, itemL).ToString();

        // 物品堆叠信息
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Volume);
        Stack.Volume = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // 是否可拾取
        itemL = excel.FindColumn(0, ExcelIdentifyRow.CanBePickedUp);
        object canBePickedUpValue = excel.GetCellValue(itemRow, itemL);
        if (canBePickedUpValue is double || canBePickedUpValue is int)
            Stack.CanBePickedUp = Convert.ToInt32(canBePickedUpValue) != 0;
        else
            Stack.CanBePickedUp = Convert.ToBoolean(canBePickedUpValue);

        // 类型标签列表
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Item_TypeTag);
        string typeTagStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_TypeTag = excel.ParseStringList(typeTagStr);

        // 材质标签列表
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Item_Material);
        string materialStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_Material = excel.ParseStringList(materialStr);

        Guid = 0;
        Debug.Log("基础数据同步成功！");
        return itemRow;
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






