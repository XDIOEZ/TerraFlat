using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class EntityItem_Stone : Item, IHealth, ILoot
{
    public Data_Creature data;
    public override ItemData itemData { get => data; set => data = (Data_Creature)value; }
    public Hp Hp { get => data.hp; set => data.hp = value; }
    public Defense Defense { get => data.defense; set => data.defense = value; }
    public UltEvent OnHpChanged { get; set; }
    public UltEvent OnDefenseChanged { get; set; }
    public Loot_List Loots { get => data.loot; set => data.loot = value; }

    public UltEvent OnDeath { get; set; }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Death()
    {
        OnDeath.Invoke();
        Destroy(gameObject);
    }
}
