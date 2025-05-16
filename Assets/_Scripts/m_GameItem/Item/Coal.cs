using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Coal : Item,IFuel
{
    public Com_ItemData _coalData;
    public override ItemData Item_Data { get { return _coalData; } set { _coalData = value as Com_ItemData; } }

      #region 燃料相关
    public float MaxBurnTime { get => maxBurnTime; set => maxBurnTime = value; }
    public float MaxTemptrue { get => maxTemptrue; set => maxTemptrue = value; }
    [Header("非序列化字段")]
    [SerializeField]
    private float maxBurnTime = 32;
    [SerializeField]
    private float maxTemptrue = 300;

    public override int SyncItemData()
    {
        int i = base.SyncItemData();

        IFuel fuel = this as IFuel;
        fuel.SyncFuelData(i);

        return i;
    }
    #endregion

    public override void Act()
    {
        Debug.Log("Coal被使用了");
        //get father name 
        string fatherName = transform.parent.gameObject.name;
        //debug father name to debug
        Debug.Log(fatherName);
    }
}
