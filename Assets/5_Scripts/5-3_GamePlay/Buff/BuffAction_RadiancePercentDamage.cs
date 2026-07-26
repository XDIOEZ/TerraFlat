using System;
using UnityEngine;

[Serializable]
public class BuffAction_RadiancePercentDamage : BuffAction
{
    [Range(0f, 1f)]
    public float maxHealthPercent = 0.05f;

    public string requiredTag = "Ghost";

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null || receiver.itemData == null)
            return;

        bool hasItemTag = receiver.itemData.Tags != null &&
                          receiver.itemData.Tags.Contains(requiredTag);
        bool hasUnityTag = receiver.gameObject.tag == requiredTag;
        if (!hasItemTag && !hasUnityTag)
            return;

        receiver.itemMods.GetMod_ByID(ModText.Hp, out DamageReceiver damageReceiver);
        if (damageReceiver == null || damageReceiver.MaxHp <= 0f)
            return;

        damageReceiver.ForceHurt(damageReceiver.MaxHp * Mathf.Clamp01(maxHealthPercent));
    }
}
