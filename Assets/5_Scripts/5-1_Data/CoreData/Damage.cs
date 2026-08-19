using MemoryPack;

/// <summary>
/// 通用数据层的四类伤害容器，所有伤害直接按切割、穿刺、劈砍、钝击保存和结算。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class Damage
{
    public float Cutting;
    public float Piercing;
    public float Chopping;
    public float Blunt;

    [MemoryPackIgnore]
    public float TotalDamage => Cutting + Piercing + Chopping + Blunt;

    /// <summary>创建全为零的四类伤害数据。</summary>
    [MemoryPackConstructor]
    public Damage()
    {
    }

    /// <summary>按切割、穿刺、劈砍、钝击顺序创建伤害数据。</summary>
    public Damage(float cutting, float piercing, float chopping, float blunt)
    {
        Cutting = cutting;
        Piercing = piercing;
        Chopping = chopping;
        Blunt = blunt;
    }

    /// <summary>四类攻击分别减去对应防御，再合计最终伤害。</summary>
    public float Return_EndDamage(Defense defense = null)
    {
        defense ??= new Defense();
        return System.Math.Max(0f, Cutting - defense.Cutting) +
               System.Math.Max(0f, Piercing - defense.Piercing) +
               System.Math.Max(0f, Chopping - defense.Chopping) +
               System.Math.Max(0f, Blunt - defense.Blunt);
    }
}
