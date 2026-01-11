
using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData
{
    public GameValue_float DeepValue = new GameValue_float();
    public float salt = 0;
    public override void Initialize_Env(EnvironmentFactors env)
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

    public override TileData Clone()
    {
        var copy = (TileData_Water)MemberwiseClone();
        if (DeepValue != null)
        {
            copy.DeepValue = new GameValue_float
            {
                BaseValue = DeepValue.BaseValue,
                BaseAdditive = DeepValue.BaseAdditive,
                AdditiveModifier = DeepValue.AdditiveModifier,
                MultiplicativeModifier = DeepValue.MultiplicativeModifier,
                FinalAdditive = DeepValue.FinalAdditive
            };
        }
        return copy;
    }

}

