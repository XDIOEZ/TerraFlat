using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "�½�Buff��Ϊ_�޸��ƶ��ٶ�", menuName = "Buff/ʳ����ֵ")]
public class BuffAction_FoodValueChange : BuffAction
{
    [Header("ʳ����ֵ�仯")]
    public Nutrition NutritionChangeValue;

    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID(ModText.Food, out Mod_Food mod);
        if (mod == null)
        {
            // Buff������û���ٶȽӿڣ�ȡ��apply
            return;
        }
        mod.Data.nutrition += NutritionChangeValue;
    }
}
