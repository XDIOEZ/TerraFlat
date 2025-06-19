
using UltEvents;
using UnityEngine;

public class AI_AttackTrigger : MonoBehaviour, ITriggerAttack
{
    // Implementing the ITriggerAttack interface members

    // Properties
    public UltEvent OnStartAttack { get; set; } = new UltEvent();
    public UltEvent OnStayAttack { get; set; } = new UltEvent();
    public UltEvent OnEndAttack { get; set; } = new UltEvent();
    public GameObject Weapon_GameObject { get; set; }

    IAttackState _attackState;

    public bool HasWeapon;
    // Methods
    public void TriggerAttack(KeyState keyState, Vector3 Target)
    {

     }

    public void SetWeapon(GameObject weapon)
    {  
        // Assign the weapon to Weapon_GameObject
        Weapon_GameObject = weapon;
        //设置为子对象
        Weapon_GameObject.transform.SetParent(transform);
        Weapon_GameObject.transform.localPosition = Vector3.zero;

        _attackState = Weapon_GameObject.GetComponent<IAttackState>();
        Weapon_GameObject.GetComponent<Item>().BelongItem = transform.parent.GetComponent<Item>();
        HasWeapon = true;
        Debug.Log($"Weapon set to: {weapon.name}");
    }
    public void StartTriggerAttack()
    {
        _attackState.StartAttack();
    }

    public void StayTriggerAttack()
    {
        _attackState.UpdateAttack();
    }

    public void StopTriggerAttack()
    {
        _attackState.EndAttack();
    }


}
