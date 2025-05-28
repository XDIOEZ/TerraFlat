using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassLand : Item
{
    public Data_GeneralItem ItemData;
    public override ItemData Item_Data { get => ItemData; set => ItemData = (Data_GeneralItem)value; }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }
}


