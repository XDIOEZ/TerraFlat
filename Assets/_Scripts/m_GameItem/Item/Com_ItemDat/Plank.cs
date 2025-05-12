using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Plank : Item,IFuel
{
    public Com_ItemData _data;

    public override ItemData Item_Data
    {
        get => _data;
        set => _data = (Com_ItemData)value;
    }

    public float MaxBurnTime { get => maxBurnTime; set => maxBurnTime = value; }
    public float MaxTemptrue { get => maxTemptrue; set => maxTemptrue = value; }
    [Header("·ÇÐòÁÐ»¯×Ö¶Î")]
    [SerializeField]
    private float maxBurnTime = 32;
    [SerializeField]
    private float maxTemptrue = 300;

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
        print("Sync Plank Data");
        return i;
    }
}
