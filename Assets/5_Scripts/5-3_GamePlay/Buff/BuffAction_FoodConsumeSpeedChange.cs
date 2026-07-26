using UnityEngine;

[System.Serializable]
public class BuffAction_FoodConsumeSpeedChange : BuffAction
{
    [Header("饥饿消耗乘算倍率（>1 加快，<1 减慢）")]
    public float ConsumeSpeedMultiplier = 1f;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null)
        {
            Debug.LogWarning("[BuffAction_FoodConsumeSpeedChange] Buff 接收者为空。");
            return;
        }

        Mod_Food mod = receiver.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        if (mod?.Data?.nutritionConsumeSpeed == null)
        {
            Debug.LogWarning("[BuffAction_FoodConsumeSpeedChange] 接收者缺少 Food 数据。");
            return;
        }

        mod.Data.nutritionConsumeSpeed.MultiplicativeModifier *= ConsumeSpeedMultiplier;
    }
}
