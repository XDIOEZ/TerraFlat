using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Coal : Item,IFuel
{
    public CoalData _coalData;
    public override ItemData Item_Data { get { return _coalData; } set { _coalData = value as CoalData; } }

    public float MaxBurnTime
    {
        get
        {
            return _coalData._maxBurnTime;
        }
        set
        {
            _coalData._maxBurnTime = value;
        }

    }
    public float MaxTemptrue
    {
        get
        {
            return _coalData._maxTempTrue;
        }
        set
        {
            _coalData._maxTempTrue = value;
        }
    }
    public override void Act()
    {
        Debug.Log("Coal被使用了");
        //get father name 
        string fatherName = transform.parent.gameObject.name;
        //debug father name to debug
        Debug.Log(fatherName);
    }
}
