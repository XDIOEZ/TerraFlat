using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WoodStick : Item
{
    /*    public WoodStickData _data;*/
    public Data_GeneralItem Data;
    public override ItemData itemData
    {
        get => Data;
        set => Data = (Data_GeneralItem)value;
    }
}
