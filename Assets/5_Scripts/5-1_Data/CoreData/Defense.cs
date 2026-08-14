using MemoryPack;

/// <summary>通用数据层的四类防御容器；旧字段仅为 MemoryPack 存档兼容保留。</summary>
[System.Serializable]
[MemoryPackable]
public partial class Defense
{
    // 旧字段顺序不可改变。
    public float defenseStrength;
    public float defenseToughness;
    public float defenseMagic;
    public float maxDefenseStrength;
    public float maxDefenseToughness;
    public float maxDefenseMagic;

    // 四类字段必须追加在旧字段后。
    public float Cutting;
    public float Piercing;
    public float Chopping;
    public float Blunt;

    [MemoryPackConstructor]
    public Defense()
    {
    }

    /// <summary>旧构造入口保留，旧防御强度迁为四类同值防御。</summary>
    public Defense(float defenseStrength, float defenseToughness)
    {
        this.defenseStrength = defenseStrength;
        this.defenseToughness = defenseToughness;
        Cutting = defenseStrength;
        Piercing = defenseStrength;
        Chopping = defenseStrength;
        Blunt = defenseStrength;
    }

    public Defense(float cutting, float piercing, float chopping, float blunt)
    {
        Cutting = cutting;
        Piercing = piercing;
        Chopping = chopping;
        Blunt = blunt;
    }

    public static Defense operator +(Defense a, Defense b)
    {
        a ??= new Defense();
        b ??= new Defense();
        return new Defense(
            a.Cutting + b.Cutting,
            a.Piercing + b.Piercing,
            a.Chopping + b.Chopping,
            a.Blunt + b.Blunt);
    }

    public static Defense operator -(Defense a, Defense b)
    {
        a ??= new Defense();
        b ??= new Defense();
        return new Defense(
            a.Cutting - b.Cutting,
            a.Piercing - b.Piercing,
            a.Chopping - b.Chopping,
            a.Blunt - b.Blunt);
    }

    public override string ToString()
    {
        return $"切割防御: {Cutting} 穿刺防御: {Piercing} 劈砍防御: {Chopping} 钝击防御: {Blunt}";
    }
}
