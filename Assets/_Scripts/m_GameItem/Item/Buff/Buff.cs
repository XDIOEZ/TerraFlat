using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Buff : Item,ISave_Load
{
    public BuffData buffData;

    public override ItemData Item_Data { get { return buffData; } set { buffData = (BuffData)value; } }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Load()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        throw new System.NotImplementedException();
    }
}
