
using UnityEngine;


public abstract class CraftingAction : ScriptableObject
{
    public string Name;
    public string Description;
    public abstract void Apply(IInventory _inventory);

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = name;
        }
    }
}
