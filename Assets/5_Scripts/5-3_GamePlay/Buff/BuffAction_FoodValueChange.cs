using UnityEngine;

[System.Serializable]
public class BuffAction_FoodValueChange : BuffAction
{
    [Header("食物数值变化")]
    public Nutrition NutritionChangeValue;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null || NutritionChangeValue == null)
            return;

        Mod_Food mod = receiver.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        if (mod?.Data?.nutrition == null)
        {
            Debug.LogWarning("[BuffAction_FoodValueChange] 接收者缺少 Food 模块。");
            return;
        }

        mod.Data.nutrition += NutritionChangeValue;
        mod.DataUpdate?.Invoke();
    }
}
