using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowInInspector]
    // ��ԭ�ȴ洢���� ItemData ���ֵ��Ϊ�洢 List<ItemData>��key Ϊ��Ʒ����
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    public float SunlightIntensity;

    public UnityEngine.Vector2 MapPosition;

    public void AddItemData(ItemData itemData)
    {
        string key = itemData.IDName;
        if (!items.TryGetValue(key, out var list))
        {
            list = new List<ItemData>();
            items[key] = list;
        }
        list.Add(itemData);
    }
}
