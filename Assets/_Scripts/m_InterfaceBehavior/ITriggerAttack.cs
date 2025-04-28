using UltEvents;
using UnityEngine;

/// <summary>
/// 定义攻击功能接口 IFunction_TriggerAttack
/// </summary>
public interface ITriggerAttack
{
    UltEvent OnStartAttack { get; set; }
    UltEvent OnStayAttack { get; set; }
    UltEvent OnEndAttack { get; set; }


    void TriggerAttack(KeyState keyState, Vector3 Target); // 执行攻击 

    public GameObject Weapon_GameObject { get; set; }

    public void SetWeapon(GameObject weapon);
}
[Tooltip("定义攻击状态枚举 KeyState")]
public enum KeyState { Start, Hold, End, Void }