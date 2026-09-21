
using MemoryPack;
using UnityEngine;

/// <summary>
/// 世界地块可提取液体的最小数据契约。玩法层只读取稳定 LiquidId，具体液体语义统一交给液体目录解析。
/// </summary>
public interface IWorldLiquidSourceData
{
    string LiquidId { get; }
}

/// <summary>
/// 旧存档和 MOD 的液体接触快照，字段顺序保持原 MemoryPack 布局。
/// 新版权威深度只来自 ChunkTerrainData，此类型不拥有世界液体状态。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData, IWorldLiquidSourceData
{
    public float LiquidDepth = 0f;
    public float salt = 0;
    public string liquidId = string.Empty; // 当前地块实际可提取的稳定液体 ID。
    [MemoryPackIgnore]
    public string LiquidId => liquidId;
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
               $"  水深基础值: {LiquidDepth:F2}\n" +  // 水深值保留2位小数
               "}";
    }

    public override TileData Clone()
    {
        var copy = new TileData_Water
        {
            ID = this.ID,
            Name = this.Name,
            TileTag = this.TileTag,
            position = this.position,
            DemolitionTime = this.DemolitionTime,
            workTime = this.workTime,
            Penalty = this.Penalty,
            IsWalkable = this.IsWalkable,
            LiquidDepth = this.LiquidDepth,
            salt = this.salt,
            liquidId = this.liquidId
        };
        return copy;
    }

}

