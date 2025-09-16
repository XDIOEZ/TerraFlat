using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "New Buff_Stamina_Change", menuName = "Buff/Buff_Stamina_Change")]
public class BuffAction_StaminaChange : BuffAction
{
    [Header("精力恢复速度")]
    [Tooltip("改变倍率")]
    public float SpeedRate;
    public override void Apply(BuffRunTime data)
    {
        data.buff_Receiver.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina, out var mod);
        if (mod == null)
        {
            // Buff接受者没有速度接口，取消apply
            return;
        }
        mod.Data.CurrentStamina += SpeedRate;
    }
}