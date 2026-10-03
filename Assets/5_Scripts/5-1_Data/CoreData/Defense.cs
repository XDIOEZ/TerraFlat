using MemoryPack;

/// <summary>通用数据层的物理防御容器。</summary>
[System.Serializable]
[MemoryPackable]
public partial class Defense
{
    public float Cutting;
    public float Piercing;
    public float Chopping;
    public float Blunt;

    [MemoryPackConstructor]
    public Defense()
    {
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
        return new Defense { Physical = a.Physical + b.Physical };
    }

    public static Defense operator -(Defense a, Defense b)
    {
        a ??= new Defense();
        b ??= new Defense();
        return new Defense { Physical = a.Physical - b.Physical };
    }

    public override string ToString()
    {
        return $"物理防御: {Physical}";
    }

    #region 物理防御兼容
    [MemoryPackIgnore]
    public float Physical
    {
        get => System.Math.Max(0f, System.Math.Max(System.Math.Max(Cutting, Piercing), System.Math.Max(Chopping, Blunt)));
        set { Cutting = Piercing = Chopping = 0f; Blunt = System.Math.Max(0f, value); }
    }
    #endregion
}
