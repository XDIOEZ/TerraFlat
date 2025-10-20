using UnityEngine;
using static Cinemachine.AxisState;

[System.Serializable]
[CreateAssetMenu(fileName = "New BuffAction_HpValueChange", menuName = "Buff/BuffAction_HpValueChange")]
public class BuffAction_HpValueChange : BuffAction
{
    [Tooltip("血量修改值")]
    public float value;
    [Tooltip("模块缓存")]
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

    public override BuffAction Clone()
    {
        var newBuff = Instantiate(this);
        DamageReceiver newMod = null;
        newBuff.mod = newMod; // 防止引用污染
        return newBuff;
    }
}