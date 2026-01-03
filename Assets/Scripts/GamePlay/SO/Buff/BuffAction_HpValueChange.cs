using UnityEngine;
using static Cinemachine.AxisState;

[System.Serializable]
public class BuffAction_HpValueChange : BuffAction
{
    [Tooltip("????????")]
    public float value;
    [Tooltip("?????")]
    public DamageReceiver mod;

    public override void Apply(BuffRunTime data)
    {
        if (mod == null)
        {
            data.buff_Receiver.itemMods.GetMod_ByID(ModText.Hp, out mod);
            if (mod == null)
            {
                Debug.LogError("BuffAction_HpValueChange: damageReceiver is null");
                return;
            }

        }
        mod.Heal(value);
    }
}