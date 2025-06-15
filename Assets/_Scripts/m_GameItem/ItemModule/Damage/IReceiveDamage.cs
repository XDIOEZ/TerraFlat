public interface IReceiveDamage
{
    Defense DefenseValue { get; set; }
    Hp Hp { get; set; }
    void ReceiveDamage(float damage);
}