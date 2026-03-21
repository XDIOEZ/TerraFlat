using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
public class Inventory_Hand : Inventory
{
    public float GetItemAmountRate = 1;

    public static Inventory PlayerHand;

    [Tooltip("在Load时调用此函数进行数据初始化（仅初始化数据和逻辑，不涉及UI）")]
    public override void InitData()
    {
        PlayerHand = this;
        base.InitData();
    }
    public override void OnValidate()
    {
        Data.Name = ModText.Hand;
    }

    public override void OnLeftClick(int index)
    {

    }
}
