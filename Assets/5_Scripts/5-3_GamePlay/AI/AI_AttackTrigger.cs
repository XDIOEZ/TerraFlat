
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


        Weapon_GameObject.GetComponent<Item>().Owner = transform.parent.GetComponent<Item>();
        HasWeapon = true;
        Debug.Log($"Weapon set to: {weapon.name}");
    }
    public void StartTriggerAttack()
    {

    }

    public void StayTriggerAttack()
    {

    }

    public void StopTriggerAttack()
    {

    }


}
