using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

    public class Wolf : Item
{
    public Data_GeneralItem Data;
    public override ItemData itemData { get => Data; set => Data = value as Data_GeneralItem; }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

 
}
