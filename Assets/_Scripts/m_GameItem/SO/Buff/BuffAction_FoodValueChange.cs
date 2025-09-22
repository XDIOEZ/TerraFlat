using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "新建Buff行为_修改移动速度", menuName = "Buff/食物数值")]
public class BuffAction_FoodValueChange : BuffAction
{
    [Header("食物数值变化")]
    public Nutrition NutritionChangeValue;

    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID(ModText.Food, out Mod_Food mod);
        if (mod == null)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }
        mod.Data.nutrition += NutritionChangeValue;
    }
}
