using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Log : Item,IFuel
{
    public Data_GeneralItem _data;

    public override ItemData itemData 
    {
        get => _data;
        set => _data = (Data_GeneralItem)value;
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
