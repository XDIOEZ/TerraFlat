using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WoodStick : Item
{
    /*    public WoodStickData _data;*/
    public Com_ItemData Data;
    public override ItemData Item_Data
    {
        get
        {
            return Data;
        }
        set
        {
            Data = (Com_ItemData)value;
        }
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
