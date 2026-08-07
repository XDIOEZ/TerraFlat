using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class GameItem : Item
{
    [SerializeField, FormerlySerializedAs("Data")]
    private Data_GeneralItem data;

    public Data_GeneralItem Data => data;
    public override ItemData itemData => data;

    protected override void SetItemData(ItemData value)
    {
        data = RequireData<Data_GeneralItem>(value);
    }
}
