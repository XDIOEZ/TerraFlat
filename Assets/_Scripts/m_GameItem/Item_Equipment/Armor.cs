using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Armor : Item
{
    public Data_Armor _Data;

    public override ItemData Item_Data
    {
        get => _Data;
        set => _Data = (Data_Armor)value;
    }

    // Start is called before the first frame update


    // Update is called once per frame
    void Update()
    {
        
    }
/*    public override Item_Data GetData()
    {
        return _Data;
    }
    public override void SetData(Item_Data data)
    {
        _Data = (ArmorData)data;
    }*/

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
