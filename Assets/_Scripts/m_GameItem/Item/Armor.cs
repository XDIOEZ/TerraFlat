using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Armor : Item
{
    public ArmorData _Data;

    public override ItemData Item_Data
    {
        get
        {
            return _Data;
        }
        set
        {
            _Data = (ArmorData)value;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

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
