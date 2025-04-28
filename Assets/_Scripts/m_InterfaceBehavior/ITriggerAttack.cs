using UltEvents;
using UnityEngine;

/// <summary>
/// ���幥�����ܽӿ� IFunction_TriggerAttack
/// </summary>
public interface ITriggerAttack
{
    UltEvent OnStartAttack { get; set; }
    UltEvent OnStayAttack { get; set; }
    UltEvent OnEndAttack { get; set; }


    void TriggerAttack(KeyState keyState, Vector3 Target); // ִ�й��� 

    public GameObject Weapon_GameObject { get; set; }

    public void SetWeapon(GameObject weapon);
}
[Tooltip("���幥��״̬ö�� KeyState")]
public enum KeyState { Start, Hold, End, Void }