using UnityEngine;

[System.Serializable]
public class BuffAction_StaminaChange : BuffAction
{
    [Header("精力变化")]
    [Tooltip("每次执行增加的精力；负数表示消耗。")]
    public float SpeedRate;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null)
            return;

        Mod_Stamina mod = receiver.itemMods.GetMod_ByID(ModText.Stamina) as Mod_Stamina;
        mod?.AddStamina(SpeedRate);
    }
}
