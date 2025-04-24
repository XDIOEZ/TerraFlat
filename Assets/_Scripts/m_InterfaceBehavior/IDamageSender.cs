
using UnityEngine;

public interface IDamageSender
{
    public bool IsDamageModeEnabled { get; set; }

    public Damage DamageValue { get; set; }

    public void OnTriggerEnter2D(Collider2D other);

    public void StartTrySendDamage();

    public void StayTrySendDamage();

    public void EndTrySendDamage();


}