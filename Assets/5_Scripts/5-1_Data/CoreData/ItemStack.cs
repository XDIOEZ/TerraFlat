using MemoryPack;
using UnityEngine;
using System;
using FastCloner.Code;
using Newtonsoft.Json;
using UnityEngine.Serialization;

[MemoryPackable]
[System.Serializable]
public partial class ItemStack
{
    [Tooltip("物品数量")]
    [SerializeField, FormerlySerializedAs("Amount"), MemoryPackIgnore, JsonIgnore]
    private float amount = 1;
    public float Amount { get => amount; set { if (amount.Equals(value)) return; amount = value; MassChanged?.Invoke(this); } }

    [Tooltip("单个物品占用体积（升）")]
    public float Volume = 1;

    [Tooltip("是否可拾取")]
    public bool CanBePickedUp = true;

    [Tooltip("单个物品重量（千克）")]
    [SerializeField, FormerlySerializedAs("Weight"), MemoryPackIgnore, JsonIgnore]
    private float weight = 1;
    public float Weight { get => weight; set { if (weight.Equals(value)) return; weight = value; MassChanged?.Invoke(this); } }

    #region 质量变化通知
    // 数量和单重原位修改也通知载荷缓存，温度等其他状态不参与失效。
    [field: NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    public event Action<ItemStack> MassChanged;
    #endregion

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
