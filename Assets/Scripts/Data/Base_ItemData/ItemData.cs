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


[MemoryPackUnion(4, typeof(Data_GeneralItem))]//通用物品数据
[MemoryPackUnion(6, typeof(Data_Player))]//玩家数据
[MemoryPackUnion(8, typeof(Data_TileMap))]//瓦片地图数据
[MemoryPackUnion(9, typeof(BlockData))]//瓦片地图数据


[MemoryPackable]
[System.Serializable]   
public  abstract partial class ItemData
{
    [Tooltip("物品名称")]
    public string IDName;

    [Tooltip("物品名称")]
    public string GameName;

    [Tooltip("物品描述")]
    [TextArea]
    public string Description = "什么都没有描述";

    [Tooltip("物品耐久度")]
    public float Durability = 1;

    [Tooltip("物品耐久度")]
    public float MaxDurability = 1;

    [Tooltip("物品标签")]
    [Obsolete("已过时，推荐使用TagDictionary，性能更优")]
    public ItemTag ItemTags;

    [Tooltip("新版Tag系统_适配新版合成表")]
    public TagDictionary Tags = new ();

    [Tooltip("物品堆叠信息")]
    public ItemStack Stack;

    [Tooltip("物品缩放")]
    public ItemTransform transform = new();

    [Tooltip("物品特殊数据")]
    public string ItemSpecialData;

    [Tooltip("全局唯一标识")]
    public int Guid;
    [ShowInInspector]
    public Dictionary<string,ModuleData> ModuleDataDic =new();

    //重写ToString方法，用于在控制台输出物品信息
    public override string ToString()
    {
        string str =
            $"物品名称：{IDName}\n" +
            $"物品描述：{Description}\n" +
            $"物品体积：{Stack.Volume}\n" +
            $"物品耐久度：{Durability}\n" +
            $"是否可拾取：{Stack.CanBePickedUp}\n" +
            $"物品标签：{Tags}\n" +
            $"物品堆叠信息：{Stack}\n" +
            $"物品特殊数据：{ItemSpecialData}\n" +
            $"全局唯一标识：{Guid}";
        return str;
    }

    public virtual int SyncData()
    {
        return 0;
    }

    public ModuleData GetModuleData_Frist(string moduleID)
    {
        foreach (var item in ModuleDataDic.Values)
        {
            if (item.ID == moduleID)
            {
                return item;
            }
        }
        Debug.LogError($"没有找到对应的模块({moduleID})数据!,检测ItemData中的Mods是否被初始化,检查mod是否被Save");
        return null;
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

    /// <summary>
    /// 判断是否包含某个类型标签（主类或子类）
    /// </summary>
    public bool HasTypeTag(string tag)
    {
        return Item_TypeTag.Contains(tag);
    }

    /// <summary>
    /// 判断是否包含某个材质
    /// </summary>
    public bool HasMaterial(string material)
    {
        return Item_Material.Contains(material);
    }
}


[System.Serializable]
[MemoryPackable]
public partial class ItemTransform
{
    [Tooltip("物品位置")]
    public Vector3 position;

    [Tooltip("物品旋转")]
    public Quaternion rotation;

    [Tooltip("物品缩放")]
    public Vector3 scale = Vector3.one;
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

[System.Serializable]
[MemoryPackable]
public partial class ModuleDatas
{
    [ShowInInspector]
    public Dictionary<string, List<ModuleData>> ModuleDataDic_ID = new();
}





