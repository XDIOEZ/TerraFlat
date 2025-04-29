
using UltEvents;

public interface IColdWeapon
{
    public Damage WeaponDamage { get; set; }
    public float MinDamageInterval { get; set; }
    public float MaxAttackDistance { get; set; }
    public float AttackSpeed { get; set; }
    public float ReturnSpeed { get; set; }
    public float SpinSpeed { get; set; }
    public float EnergyCostSpeed { get; set; }
    //上一次造成伤害的时间
    public float LastDamageTime { get; set; }
    //武器的最大伤害次数
    public float MaxDamageCount { get; set; }

}

<<<<<<< Updated upstream
public interface IDamager
{
    IDamageSender Sender { get; set; }
=======
>>>>>>> Stashed changes

