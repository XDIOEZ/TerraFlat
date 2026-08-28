
using MemoryPack;
using UnityEngine;

/// <summary>
/// 水体地块数据；海洋水深由高度层统一换算，河流仍使用水文系统提供的独立深度。
/// 海平面内的归一化高度采用平方曲线，使近岸到深海的高低差更明显。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData
{
    private const float SeaLevel = 0.5f;

    public float deepValue = 0f;
    public float salt = 0;
    public override void Initialize_Env(EnvironmentLayers layers, int x, int y)
    {
        if (layers == null || !layers.Contains(x, y))
            return;

        deepValue = CalculateDepthFromHeight(layers.Height[x, y]);
    }

    /// <summary>将海平面内的高度平方后反算水深，保持岸线位置并扩大海底深度差。</summary>
    public static float CalculateDepthFromHeight(float height)
    {
        float normalizedHeight = Mathf.Clamp01(height / SeaLevel);
        return 1f - normalizedHeight * normalizedHeight;
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
               $"  水深基础值: {deepValue:F2}\n" +  // 水深值保留2位小数
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
            deepValue = this.deepValue,
            salt = this.salt
        };
        return copy;
    }

}

