
public interface IColdWeapon
{
    public Damage WeaponDamage { get; set; }
    public float MinDamageInterval { get; set; }
    public float MaxAttackDistance { get; set; }
    public float AttackSpeed { get; set; }
    public float ReturnSpeed { get; set; }
    public float SpinSpeed { get; set; }
    public float EnergyCostSpeed { get; set; }

}

public interface IDamager
{
    IDamageSender Sender { get; set; }

    void AttackStart()
    {
         Sender.StartTrySendDamage();
    }

    void AttackUpdate()
    {
        // Attack logic
    }
    void AttackEnd()
    {
        // Attack logic
        Sender.EndTrySendDamage();
    }
}