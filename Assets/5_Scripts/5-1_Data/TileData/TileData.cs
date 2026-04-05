using MemoryPack;
using UnityEngine;


[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(54, typeof(TileData_Grass))]//草地数据
[MemoryPackUnion(55, typeof(TileData_Water))]//水地数据
[MemoryPackUnion(56, typeof(TileData_Universal))]//通用地块数据
public abstract partial class TileData
{
    //物品的绘制物块 用于实现
    public string ID;
    //对应的物品名字--用于获取物品中的方法
    public string Name;
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
    public bool IsWalkable = true;


    // 虚函数：根据环境层初始化
    public virtual void Initialize_Env(EnvironmentLayers layers, int x, int y) { }

    /// <summary>
    /// 为运行时创建一份浅量的深拷贝（手写，避免通用深拷贝插件开销）
    /// </summary>
    public abstract TileData Clone();
    /// <summary>
    /// 重写ToString方法，返回对象的详细信息
    /// </summary>
    /// <returns>包含所有字段信息的字符串</returns>
    public override string ToString()
    {
        return $"TileData {{\n" +
        $"地块基础名称: {ID},\n" +
        $" 对应物品名称: {Name},\n" +
        $"地块标签: {TileTag},\n" +
        $" 地块位置: ({position.x}, {position.y}, {position.z}),\n" +
        $"拆除所需时间: {DemolitionTime:F2},\n" + // 保留 2 位小数，数值更直观
        $"当前拆除时间: {workTime:F2}\n" +
        "}";
    }
}

