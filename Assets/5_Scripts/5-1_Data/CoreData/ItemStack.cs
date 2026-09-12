using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class ItemStack
{
    [Tooltip("物品数量")]
    public float Amount = 1;//物体的数量

    [Tooltip("单个物品占用体积（升）")]
    public float Volume = 1;

    [Tooltip("是否可拾取")]
    public bool CanBePickedUp = true;

    [Tooltip("单个物品重量（千克）")]
    public float Weight = 1;

    [Tooltip("是否允许同类物品堆叠")]
    public bool Stackable = true;

    [MemoryPackIgnore]
    [Tooltip("当前总体积")]
    public float CurrentVolume
    {
        get
        {
            return Amount * Volume;
        }
    }

    [MemoryPackIgnore]
    [Tooltip("当前总重量（千克）")]
    public float CurrentWeight => Amount * Weight;


    public override string ToString()
    {
        return string.Format("物体数量:{0}", Amount);
    }

}
