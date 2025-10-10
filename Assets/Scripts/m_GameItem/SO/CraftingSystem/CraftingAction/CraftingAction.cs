
using UnityEngine;


public abstract class CraftingAction : ScriptableObject
{
    public string Name;
    public string Description;
    [Header("操作设置")]
    [Tooltip("操作索引")]
    public int slotIndex = 0; // 操作的插槽索引，从0开始
    [Tooltip("操作容器名字,为空默认使用字典第一个")]
    public string InventoryName; // 操作的容器名字，为空默认使用字典第一个

    public abstract void Apply(IInventory _inventory);

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = name;
        }
    }
}
