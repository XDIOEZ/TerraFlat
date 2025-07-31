using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Item_For_Module : Item
{
    public Data_GeneralItem Data;
    public override ItemData Item_Data { get { return Data; }  set { Data = (Data_GeneralItem)value; } }
}
