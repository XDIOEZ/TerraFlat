using System.Collections.Generic;
using MemoryPack;

/// <summary>
/// 通用数据层的四类伤害容器。旧字段只为 MemoryPack 存档兼容保留，新结算仅使用四类数值。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class Damage
{
    // 旧字段顺序不可改变。
    public float PhysicalDamage;
    public float ArmorBreaking;
    public float MagicDamage;
    public List<string> DamageType;

    // 四类字段必须追加在旧字段后。
    public float Cutting;
    public float Piercing;
    public float Chopping;
    public float Blunt;

    [MemoryPackIgnore]
    public float TotalDamage => Cutting + Piercing + Chopping + Blunt;

    [MemoryPackConstructor]
    public Damage()
    {
        DamageType = new List<string>();
    }

    public Damage(float cutting, float piercing, float chopping, float blunt) : this()
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
