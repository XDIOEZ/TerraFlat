
using UnityEngine;


public abstract class CraftingAction : ScriptableObject
{
    public string Name;
    public string Description;
    [Header("操作设置")]
    public int slotIndex = 0; // 操作的插槽索引，从0开始

    public abstract void Apply(ItemSlot itemSlot);

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = name;
        }
    }
}
