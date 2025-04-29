using System.Collections;
using UltEvents;
using UnityEngine;

public class AI_AttackTrigger : MonoBehaviour, ITriggerAttack
{
    // Implementing the ITriggerAttack interface members

    // Properties
    public UltEvent OnStartAttack { get; set; }
    public UltEvent OnStayAttack { get; set; }
    public UltEvent OnEndAttack { get; set; }
    public GameObject Weapon_GameObject { get; set; }

    // Methods
    public void TriggerAttack(KeyState keyState, Vector3 Target)
    {
        // Add logic for triggering an attack
        Debug.Log($"Triggering attack with KeyState: {keyState} and Target: {Target}");
    }

    public void SetWeapon(GameObject weapon)
    {
        // Assign the weapon to Weapon_GameObject
        Weapon_GameObject = weapon;
        Debug.Log($"Weapon set to: {weapon.name}");
    }
}
