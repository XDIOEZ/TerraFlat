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
    
    void StartTriggerAttack();

    void StayTriggerAttack();

    void StopTriggerAttack();

    public GameObject Weapon_GameObject { get; set; }

    public void SetWeapon(GameObject weapon);

    // 修复后的代码
    public void DestroyWeapon()
    {
        if (Weapon_GameObject != null)
        {
            Object.Destroy(Weapon_GameObject); // 使用类型名 Object 调用 Destroy 方法
            Weapon_GameObject.SetActive(false);
            Weapon_GameObject = null;
        }
    }

    public void CreateWeapon(ItemData weapon)
    {
        //根據傳入的武器數據 創建一個武器物件
        GameObject weapon_GameObject = GameItemManager.Instance.InstantiateItem(weapon).gameObject;
        weapon.Stack.CanBePickedUp = false;
        SetWeapon(weapon_GameObject);
    }

    public void GetItemWeapon(Item weapon)
    {
        weapon.Item_Data.Stack.CanBePickedUp = false;
        SetWeapon(weapon.gameObject);
    }


}
[Tooltip("定义攻击状态枚举 KeyState")]
public enum KeyState { Start, Hold, End, Void }