using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class Tree_Data : ItemData
{
    [Header("树数据")]

    [Tooltip("血量")]
    public Hp hp = new Hp(100);

    [Tooltip("防御")]
    public Defense defense = new Defense(5, 20);

    [Tooltip("掉落物品")]
    public List_Loot loot;

    [Tooltip("树的相关数据")]
    public ItemValues ItemDataValue;

   


    public override int SyncData()
    {
        int itemRow = base.SyncData();
        var excel = m_ExcelManager.Instance;

        // 同步血量
        hp.maxValue = excel.GetConvertedValue<float>("MaxHp", itemRow, 0.0f);
        hp.value = excel.GetConvertedValue<float>("Hp", itemRow, 0.0f);

        // 同步防御属性
        defense.defenseStrength = excel.GetConvertedValue<float>("DefenseStrength", itemRow, 0.0f);
        defense.defenseToughness = excel.GetConvertedValue<float>("DefenseToughness", itemRow, 0.0f);
        defense.defenseMagic = excel.GetConvertedValue<float>("DefenseMagic", itemRow, 0.0f);

        defense.maxDefenseStrength = excel.GetConvertedValue<float>("MaxDefenseStrength", itemRow, 0.0f);
        defense.maxDefenseToughness = excel.GetConvertedValue<float>("MaxDefenseToughness", itemRow, 0.0f);
        defense.maxDefenseMagic = excel.GetConvertedValue<float>("MaxDefenseMagic", itemRow, 0.0f);

  

        string typeTagStr = excel.GetConvertedValue<string>("Weaknesses", itemRow, string.Empty);
        hp.Weaknesses = excel.ParseStringList(typeTagStr);

        string rawLootData = excel.GetConvertedValue<string>("Loots_Death", itemRow, string.Empty);
        loot.GetLoot("Loots_Death").lootList = excel.Parse(rawLootData);


        loot.GetLoot("Loots_Production").lootList 
            = excel.Parse(excel.GetConvertedValue<string>("Loots_Production", itemRow, string.Empty));

        return itemRow;
    }

}