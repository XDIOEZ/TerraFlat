using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DamageReceiver;
[CreateAssetMenu(fileName = "New LootDropAdjuster", menuName = "ModActions/战利品调整", order = 0)]
public class LootDropAdjuster : ModAction
{
    [Header("向死亡掉落物中传入的额外物品")]
    public LootEntry dropLoot;
    public override void Action(Item ModOwner, Module module, Item targetItem = null)
    {
        ModOwner.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp, out var damageReceiver);
        damageReceiver.Data.LootTable.Add(dropLoot);
    }
}