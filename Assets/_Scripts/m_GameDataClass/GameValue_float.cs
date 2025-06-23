using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class GameValue_float
{
    public float BaseValue = 1;                      // 基础值
    public float BaseAdditive = 0;               // 基础加算项（装备、Buff等）
    public float AdditiveModifier = 0;           // 百分比加算项（技能、被动加成等）
    public float MultiplicativeModifier = 1;     // 乘算修正（暴击、特殊状态倍率）
    public float FinalAdditive = 0;              // 最终加算项（结算时额外增加的数值）

    // 最终数值计算公式
    public float Value
    {
        get
        {
            return (BaseValue + BaseAdditive) * (1 + AdditiveModifier) * MultiplicativeModifier + FinalAdditive;
        }
    }
}
