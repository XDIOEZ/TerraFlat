
using MemoryPack;
using NaughtyAttributes;

[MemoryPackable]
[System.Serializable]
public partial class Data_GeneralItem : ItemData
{
    public string code;

    public ItemValues values;

    public override int SyncData()
    {
       int itemRow = base.SyncData();

        code = m_ExcelManager.Instance.GetConvertedValue<string>("code", itemRow);

        return itemRow;
    }
}