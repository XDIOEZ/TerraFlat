using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stone : Item
{
    public Data_GeneralItem _data;

    public override ItemData Item_Data
    {
        get => _data;
        set => _data = (Data_GeneralItem)value;
    }
/*    public override Item_Data GetData()
    {
        Debug.Log($"��ȡ{name}������....");
        return _data;
    }
    public override void SetData(Item_Data data)
    {
        Debug.Log($"����{name}������....");
        _data = (StoneData)data;
    }*/

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    // Start is called before the first frame updat

    // Update is called once per frame
    void Update()
    {

    }
}
