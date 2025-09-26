
using UnityEngine;


public abstract class CraftingAction : ScriptableObject
{
    public string Name;
    public string Description;
    [Header("��������")]
    public int slotIndex = 0; // �����Ĳ����������0��ʼ

    public abstract void Apply(ItemSlot itemSlot);

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = name;
        }
    }
}
