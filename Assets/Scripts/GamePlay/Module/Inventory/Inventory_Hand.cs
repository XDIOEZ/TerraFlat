using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Inventory_Hand : Inventory
{
    public float GetItemAmountRate = 1;

    public static Inventory PlayerHand;

    public override void Awake()
    {
        base.Awake();
        PlayerHand = this;
    }

    public override void BindController()
    {
        
    }

    public override void OnValidate()
    {
        Data.Name = ModText.Hand;
    }

    public override void OnLeftClick(int index)
    {

    }
}
