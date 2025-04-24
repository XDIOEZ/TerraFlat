using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ItemValues
{
    public List<ItemValue> ItemValue_List;

    public ItemValue GetItemValue(string valueName)
    {
        foreach (ItemValue itemValue in ItemValue_List)
        {
            if (itemValue.ValueName == valueName)
            {
                return itemValue;
            }
        }
        return null;
    }

    public void SetItemValue(ItemValue itemValue)
    {
        foreach (var existingItem in ItemValue_List)
        {
            if (existingItem.ValueName == itemValue.ValueName)
            {
                existingItem.CurrentValue = itemValue.CurrentValue;
                return;
            }
        }

        // �������ڣ�������µ�ֵ
        ItemValue_List.Add(itemValue);
    }
}
