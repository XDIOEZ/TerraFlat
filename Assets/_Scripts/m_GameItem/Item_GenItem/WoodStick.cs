using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WoodStick : Item
{
    /*    public WoodStickData _data;*/
    public Data_GeneralItem Data;
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_GeneralItem)value;
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}
