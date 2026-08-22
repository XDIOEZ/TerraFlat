using MemoryPack;

/// <summary>通用数据层的四类防御容器。</summary>
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
