using UnityEngine;
using static Cinemachine.AxisState;

[System.Serializable]
[CreateAssetMenu(fileName = "New BuffAction_HpValueChange", menuName = "Buff/BuffAction_HpValueChange")]
public class BuffAction_HpValueChange : BuffAction
{
    [Tooltip("血量修改值")]
    public float value;
    [Tooltip("模块缓存")]
    public DamageReceiver damageReceiver;

    public override void Apply(BuffRunTime data)
    {
        if (damageReceiver == null)
        {
            data.buff_Receiver.itemMods.GetMod_ByID(ModText.Hp, out damageReceiver);
            if (damageReceiver == null)
            {
                Debug.LogError("BuffAction_HpValueChange: damageReceiver is null");
                return;
            }

        }
        damageReceiver.Heal(value);
    }
}