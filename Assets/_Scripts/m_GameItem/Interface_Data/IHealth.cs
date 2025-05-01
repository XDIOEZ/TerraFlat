
using UltEvents;

public interface IHealth
{
    Hp Hp { get; set; }

    Defense Defense { get; set; }

    public float GetDamage(Damage damage)
    {
        float damageValue = damage.Return_EndDamage(Defense);
        Hp.Value -= damageValue;

        if (Hp.Value <= 0)
        {
            Death();
        }
        return damageValue;
    }

    public virtual void ChangeDefense(Defense value)
    {
        Defense += value;
        OnDefenseChanged.Invoke();
    }

    public void Death();

    public UltEvent OnHpChanged { get; set; }

    public UltEvent OnDefenseChanged { get; set; }
}
