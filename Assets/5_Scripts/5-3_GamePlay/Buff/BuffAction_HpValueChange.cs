using UnityEngine;

[System.Serializable]
public class BuffAction_HpValueChange : BuffAction
{
    [Tooltip("每次恢复的生命值")]
    public float value;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null || value <= 0f)
            return;

        DamageReceiver mod = receiver.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
        if (mod == null)
        {
            Debug.LogWarning($"[BuffAction_HpValueChange] {receiver.name} 缺少 DamageReceiver。");
            return;
        }

        mod.Heal(value);
    }
}
