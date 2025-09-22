using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DamageReceiver;
[CreateAssetMenu(fileName = "New LootDropAdjuster", menuName = "ModActions/ս��Ʒ����", order = 0)]
public class LootDropAdjuster : ModAction
{
    [Header("�������������д���Ķ�����Ʒ")]
    public LootEntry dropLoot;
    public override void Action(Item ModOwner, Module module, Item targetItem = null)
    {
        ModOwner.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp, out var damageReceiver);
        damageReceiver.Data.LootTable.Add(dropLoot);
    }
}