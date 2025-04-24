using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassLand : Item
{
    public Com_ItemData ItemData;
    public override ItemData Item_Data { get => ItemData; set => ItemData = (Com_ItemData)value; }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}


