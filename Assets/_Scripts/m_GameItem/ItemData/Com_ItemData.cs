
using MemoryPack;
using NaughtyAttributes;

[MemoryPackable]
[System.Serializable]
public partial class Com_ItemData : ItemData
{
    public string code;

    [Button("从Excel处同步数据")]
    public override int SyncData()
    {
       int itemRow = base.SyncData();

        code = m_ExcelManager.Instance.GetConvertedValue<string>("code", itemRow);

return itemRow;
    }
}