using NaughtyAttributes;
using UnityEngine;

public class DamageSender_ColdWeapon : MonoBehaviour, IDamageSender
{
    [ShowNativeProperty]
    public bool IsDamageModeEnabled { get; set; }

    [ShowNativeProperty]
    public Damage DamageValue { get; set; }

    private Collider2D weaponCollider;
    public Collider2D WeaponCollider
    {
        get
        {
            if (weaponCollider == null)
            {
                weaponCollider = GetComponent<Collider2D>();
            }
            return weaponCollider;
        }

        set
        {
            weaponCollider = value;
        }
    }

    public void StartTrySendDamage()
    {
        WeaponCollider.enabled = true;
    }

    public void EndTrySendDamage()
    {
        WeaponCollider.enabled = false;
    }

    public void StayTrySendDamage()
    {
        throw new System.NotImplementedException();
    }

    public void OnTriggerEnter2D(Collider2D other)
    {
        IDamageReceiver receiver = other.GetComponent<IDamageReceiver>();
        if (receiver == null) return;

        // ��ȡ����Ӵ��㣨���е㣩
        Vector2 hitPoint = other.ClosestPoint(transform.position);

        // �������ذ汾���������е�
        receiver.TakeDamage(DamageValue, hitPoint);
    }
}
