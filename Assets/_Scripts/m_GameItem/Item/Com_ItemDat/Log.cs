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

    public float MaxBurnTime => 32;

    public float MaxTemptrue => 300;

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}
