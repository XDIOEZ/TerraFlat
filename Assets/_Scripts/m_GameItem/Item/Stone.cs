using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stone : Item
{
    public Com_ItemData _data;

    public override ItemData Item_Data
    {
        get
        {
            return _data;
        }
        set
        {
            _data = (Com_ItemData)value;
        }
    }
/*    public override Item_Data GetData()
    {
        Debug.Log($"获取{name}数据中....");
        return _data;
    }
    public override void SetData(Item_Data data)
    {
        Debug.Log($"设置{name}数据中....");
        _data = (StoneData)data;
    }*/

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
