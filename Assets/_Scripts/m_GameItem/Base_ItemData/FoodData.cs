
using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class Data_Food : ItemData
{
    //public Nutrition NutritionData = new Nutrition(0, 0);

    public override int SyncData()
    {
        int itemRow = base.SyncData();
        var excel = m_ExcelManager.Instance;

        //NutritionData.Max_Carbohydrates = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxFood, itemRow, 0.0f);
        //NutritionData.Max_Water = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxWater, itemRow, 0.0f);

        return itemRow;
    }
}