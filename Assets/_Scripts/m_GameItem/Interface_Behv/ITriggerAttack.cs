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
    
    void StartTriggerAttack();

    void StayTriggerAttack();

    void StopTriggerAttack();

    public GameObject Weapon_GameObject { get; set; }

    public void SetWeapon(GameObject weapon);

    // �޸���Ĵ���
    public void DestroyWeapon()
    {
        if (Weapon_GameObject != null)
        {
            Object.Destroy(Weapon_GameObject); // ʹ�������� Object ���� Destroy ����
            Weapon_GameObject.SetActive(false);
            Weapon_GameObject = null;
        }
    }

    public void CreateWeapon(ItemData weapon)
    {
        //����������������� ����һ���������
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
[Tooltip("���幥��״̬ö�� KeyState")]
public enum KeyState { Start, Hold, End, Void }