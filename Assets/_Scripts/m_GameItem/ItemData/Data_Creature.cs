
using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class Data_Creature : ItemData
{
    #region 生命
    [Tooltip("血量")]
    public Hp hp = new Hp(30);

    [Tooltip("防御力")]
    public Defense defense = new(5, 5);
    #endregion

    #region 攻击
    [Tooltip("进食速度")]
    public float EatingSpeed = 1;
    #endregion

    #region 速度
    [Tooltip("最大移动速度")]
    public float speed_Max = 3;
    [Tooltip("速度")]
    public float speed = 3;
    [Tooltip("奔跑速度")]
    public float runSpeed = 6;
    #endregion

    #region 精力
    [Tooltip("精力值")]
    public float stamina = 100;
    [Tooltip("精力上限")]
    public float stamina_Max = 100;
    [Tooltip("精力恢复速度")]
    public float staminaRecoverySpeed = 1;
    #endregion

    #region 食物
    [Tooltip("饥饿值/营养值")]
    public Nutrition NutritionData = new Nutrition(100, 100);
    #endregion

    #region 生产

    [Tooltip("生产进度")]
    public float progress = 0;
    [Tooltip("生产进度上限")]
    public float progress_Max = 100;
    [Tooltip("生产速度")]
    public float productionSpeed = 1;

    #endregion

    #region 感知
    [Tooltip("感知范围")]
    public float sightRange = 10;
    #endregion

    #region 库存

    [ShowNonSerializedField]
    [Tooltip("库存数据")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    #region 数据
    [Tooltip("物体数据")]
    public ItemValues ItemDataValue;

    [Tooltip("掉落物品")]
    public List_Loot loot;
    #endregion

    #region 团队
    public string TeamID = "";

    public Dictionary<string, RelationType> Relations = new Dictionary<string, RelationType>();
    #endregion

    public override int SyncData()
    {
        int itemRow = base.SyncData();
        var excel = m_ExcelManager.Instance;

        // 同步血量
        hp.maxValue = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxHp, itemRow, 0.0f);
        hp.Value = excel.GetConvertedValue<float>(ExcelIdentifyRow.Hp, itemRow, 0.0f);

        // 同步防御属性
        defense.defenseStrength = excel.GetConvertedValue<float>(ExcelIdentifyRow.DefenseStrength, itemRow, 0.0f);
        defense.defenseToughness = excel.GetConvertedValue<float>(ExcelIdentifyRow.DefenseToughness, itemRow, 0.0f);
        defense.defenseMagic = excel.GetConvertedValue<float>(ExcelIdentifyRow.DefenseMagic, itemRow, 0.0f);

        defense.maxDefenseStrength = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxDefenseStrength, itemRow, 0.0f);
        defense.maxDefenseToughness = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxDefenseToughness, itemRow, 0.0f);
        defense.maxDefenseMagic = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxDefenseMagic, itemRow, 0.0f);

        string typeTagStr = excel.GetConvertedValue<string>(ExcelIdentifyRow.Weaknesses, itemRow, string.Empty);
        hp.Weaknesses = excel.ParseStringList(typeTagStr);

        string rawLootData = excel.GetConvertedValue<string>(ExcelIdentifyRow.LootsDeath, itemRow, string.Empty);
        loot.GetLoot("Loots_Death").lootList = excel.Parse(rawLootData);

        loot.GetLoot("Loots_Production").lootList =
            excel.Parse(excel.GetConvertedValue<string>(ExcelIdentifyRow.LootsProduction, itemRow, string.Empty));


        NutritionData.MaxFood = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxFood, itemRow, 0.0f);
        NutritionData.MaxWater = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxWater, itemRow, 0.0f);

        return itemRow;
    }

}