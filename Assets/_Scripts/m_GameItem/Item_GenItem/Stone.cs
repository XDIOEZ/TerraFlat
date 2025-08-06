using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stone : Item
{
    public Data_GeneralItem _data;

    public override ItemData itemData
    {
        get => _data;
        set => _data = (Data_GeneralItem)value;
    }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

}
