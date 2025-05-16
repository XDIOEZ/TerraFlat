using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Log : Item,IFuel
{
    public Com_ItemData _data;

    public override ItemData Item_Data 
    {
        get => _data;
        set => _data = (Com_ItemData)value;
    }

    #region ȼ�����
    public float MaxBurnTime { get => maxBurnTime; set => maxBurnTime = value; }
    public float MaxTemptrue { get => maxTemptrue; set => maxTemptrue = value; }
    [Header("�����л��ֶ�")]
    [SerializeField]
    private float maxBurnTime = 32;
    [SerializeField]
    private float maxTemptrue = 300;

    public override int SyncItemData()
    {
        int i = base.SyncItemData();

        ((IFuel)this).SyncFuelData(i);

        return i;
    }
    #endregion

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}
