
using UnityEngine;


public abstract class CraftingAction : ScriptableObject
{
    public string Name;
    public string Description;
    [Header("��������")]
    [Tooltip("��������")]
    public int slotIndex = 0; // �����Ĳ����������0��ʼ
    [Tooltip("������������,Ϊ��Ĭ��ʹ���ֵ��һ��")]
    public string InventoryName; // �������������֣�Ϊ��Ĭ��ʹ���ֵ��һ��

    public abstract void Apply(IInventory _inventory);

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = name;
        }
    }
}
