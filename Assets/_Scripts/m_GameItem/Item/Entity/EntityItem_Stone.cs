using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class EntityItem_Stone : Item, IHealth, ILoot
{
    public Data_Creature data;
    public override ItemData Item_Data { get => data; set => data = (Data_Creature)value; }
    public Hp Hp { get => data.hp; set => data.hp = value; }
    public Defense Defense { get => data.defense; set => data.defense = value; }
    public UltEvent OnHpChanged { get; set; }
    public UltEvent OnDefenseChanged { get; set; }
    public List_Loot Loots { get => data.loot; set => data.loot = value; }

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

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
