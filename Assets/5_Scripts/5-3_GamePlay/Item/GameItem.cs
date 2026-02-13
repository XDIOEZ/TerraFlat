using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameItem : Item
{
    public Data_GeneralItem Data;
    public override ItemData itemData { get { return Data; }  set { Data = (Data_GeneralItem)value; } }
}
