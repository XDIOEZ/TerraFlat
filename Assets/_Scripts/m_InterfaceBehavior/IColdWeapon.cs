
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
    //��һ������˺���ʱ��
    public float LastDamageTime { get; set; }
    //����������˺�����
    public float MaxDamageCount { get; set; }

}

<<<<<<< Updated upstream
public interface IDamager
{
    IDamageSender Sender { get; set; }
=======
>>>>>>> Stashed changes

