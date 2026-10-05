using MemoryPack;

/// <summary>
/// 通用数据层的物理伤害兼容容器，旧四槽只用于读取历史存档。
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

    #region 物理伤害兼容

    /// <summary>旧存档的攻击分量合成物理伤害，再抵扣一次物理防御。</summary>
    public float Return_EndDamage(Defense defense = null)
    {
        defense ??= new Defense();
        return System.Math.Max(0f, Physical - defense.Physical);
    }

    [MemoryPackIgnore]
    public float Physical
    {
        get => System.Math.Max(0f, Cutting) + System.Math.Max(0f, Piercing) + System.Math.Max(0f, Chopping) + System.Math.Max(0f, Blunt);
        set { Cutting = Piercing = Chopping = 0f; Blunt = System.Math.Max(0f, value); }
    }
    #endregion
}
