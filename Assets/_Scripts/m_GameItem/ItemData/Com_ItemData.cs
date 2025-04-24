
using MemoryPack;
using NaughtyAttributes;

[MemoryPackable]
[System.Serializable]
public partial class Com_ItemData : ItemData
{
    public string code;

    [Button("��Excel��ͬ������")]
    public override int SyncData()
    {
       int itemRow = base.SyncData();

        code = m_ExcelManager.Instance.GetConvertedValue<string>("code", itemRow);

return itemRow;
    }
}