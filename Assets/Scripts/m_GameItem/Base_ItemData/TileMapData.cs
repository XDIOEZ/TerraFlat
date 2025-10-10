using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [HideInInspector]
    [SerializeField]
    public Dictionary<Vector2Int, List<TileData>> TileData = new();

    [Tooltip("地图的位置")]
    public Vector2Int position = new Vector2Int(0,0);

    public bool TileLoaded = false;

    public EnvironmentFactors[,] EnvironmentData = new EnvironmentFactors[0, 0];
}

[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(54, typeof(TileData_Grass))]//墙壁数据
[MemoryPackUnion(55, typeof(TileData_Water))]//材料数据
public  abstract partial class TileData
{
    //物品的绘制物块 用于实现
    public string Name_TileBase;
    //对应的物品名字--用于获取物品中的方法
    public string Name_ItemName;
    //地块的Tag
    public string TileTag = "";
    //地块所在位置
    public Vector3Int position;
    //拆除所需时间
    public float DemolitionTime;
    //当前拆除的时间
    public float workTime;
    //地块移动权重
    public uint Penalty = 1000;


    // 虚函数：根据环境初始化
    public virtual void Initialize(EnvironmentFactors env) { }
    /// <summary>
    /// 重写ToString方法，返回对象的详细信息
    /// </summary>
    /// <returns>包含所有字段信息的字符串</returns>
    public override string ToString()
    {
        return $"TileData {{\n" +
        $"地块基础名称: {Name_TileBase},\n" +
        $" 对应物品名称: {Name_ItemName},\n" +
        $"地块标签: {TileTag},\n" +
        $" 地块位置: ({position.x}, {position.y}, {position.z}),\n" +
        $"拆除所需时间: {DemolitionTime:F2},\n" + // 保留 2 位小数，数值更直观
        $"当前拆除时间: {workTime:F2}\n" +
        "}";
    }
}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Grass : TileData
{
    public GameValue_float FertileValue = new GameValue_float();
}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData
{
    public GameValue_float DeepValue = new GameValue_float();

    public override void Initialize(EnvironmentFactors env)
    {
        // 高度 0.5 → 深度 0
        // 高度 0   → 深度 1
        DeepValue.BaseValue = (0.5f - env.Hight) / 0.5f;
    }
    /// <summary>
    /// 重写ToString方法，返回水地块的详细信息（中文格式）
    /// </summary>
    /// <returns>包含父类信息和水深值的格式化字符串</returns>
    public override string ToString()
    {
        // 处理父类字符串，移除首尾的大括号并保留原有缩进
        string parentInfo = base.ToString()
            .TrimStart('{', ' ')
            .TrimEnd('}')
            .Replace("\n  ", "\n    "); // 父类字段缩进增加一级，与子类字段区分

        return $"TileData_Water {{\n" +
               $"  {parentInfo},\n" +  // 继承父类的中文信息
               $"  水深基础值: {DeepValue.BaseValue:F2}\n" +  // 水深值保留2位小数
               "}";
    }

}

