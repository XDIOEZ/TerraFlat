using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Armor : Item
{
    public Data_Armor _Data;

    public override ItemData itemData
    {
        get => _Data;
        set => _Data = (Data_Armor)value;
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }
}

public enum ArmorType
{
    Head,
    Body,
    Leg,
    Feet,
}
