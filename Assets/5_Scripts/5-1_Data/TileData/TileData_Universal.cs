using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Universal : TileData
{
    /// <summary>
    /// 通用地块的数据结构：
    /// - 暂时不增加额外字段，完全复用基类 TileData 的通用属性（ID、Name、Tag、Penalty 等）。
    /// - 如需扩展（例如通用强度、通用权重等），可以在这里增加字段并在 Clone 中处理深拷贝。
    /// </summary>
    /// 
    public override TileData Clone()
    {
        // 当前没有需要手动深拷贝的引用类型字段，直接 MemberwiseClone 即可。
        return (TileData_Universal)MemberwiseClone();
    }
}
