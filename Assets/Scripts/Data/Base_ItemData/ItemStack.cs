using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class ItemStack
{
    [Tooltip("物品数量")]
    public float Amount = 1;//物体的数量

    [Tooltip("物品体积")]
    // 公共浮点型变量Volume，用于存储物品的体积或其他相关数值
    public float Volume = 1;

    [Tooltip("是否可拾取")]
    public bool CanBePickedUp = true;

    [MemoryPackIgnore]
    [Tooltip("当前总体积")]
    public float CurrentVolume
    {
        get
        {
            return Amount * Volume;
        }
    }


    public override string ToString()
    {
        return string.Format("物体数量:{0}", Amount);
    }

}