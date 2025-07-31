using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowInInspector]
    // 将原先存储单个 ItemData 的字典改为存储 List<ItemData>，key 为物品名称
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    public float SunlightIntensity;
    // 说明：在保存物品时，同一名称的物品会存储在同一 List 中，
    // 方便后续加载时批量实例化并赋值
}
