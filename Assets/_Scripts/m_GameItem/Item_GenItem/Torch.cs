using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Torch : Item, IFuel
{
    public Data_GeneralItem data;

    public float MaxBurnTime { get => maxBurnTime; set => maxBurnTime = value; }
    public float MaxTemptrue { get => maxTemptrue; set => maxTemptrue = value; }

    [Header("非序列化字段||物体固定特性")]
    [SerializeField]
    private float maxBurnTime = 32;
    [SerializeField]
    private float maxTemptrue = 300;


    public override ItemData Item_Data
    {
        get => data;
        set => data = (Data_GeneralItem)value;
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public override int SyncItemData()
    {
        int i = base.SyncItemData();

        var manager = m_ExcelManager.Instance;

        MaxBurnTime = manager.GetConvertedValue<float>(
        "MaxBurnTime", i, 0.0f);
        MaxTemptrue = manager.GetConvertedValue<float>(
        "MaxTemptrue", i, 0.0f);
        return i;
    }
}
