
using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class Data_Creature : ItemData
{
    #region ����
    [Tooltip("Ѫ��")]
    public Hp hp = new Hp(30);

    [Tooltip("������")]
    public Defense defense = new(5, 5);
    #endregion

    #region ����
    [Tooltip("��ʳ�ٶ�")]
    public float EatingSpeed = 1;
    #endregion

    #region �ٶ�
    [Tooltip("����ƶ��ٶ�")]
    public float speed_Max = 3;
    [Tooltip("�ٶ�")]
    public float speed = 3;
    [Tooltip("�����ٶ�")]
    public float runSpeed = 6;
    #endregion

    #region ����
    [Tooltip("����ֵ")]
    public float stamina = 100;
    [Tooltip("��������")]
    public float stamina_Max = 100;
    [Tooltip("�����ָ��ٶ�")]
    public float staminaRecoverySpeed = 1;
    #endregion

    #region ʳ��
    [Tooltip("����ֵ/Ӫ��ֵ")]
    public Nutrition NutritionData = new Nutrition(100, 100);
    #endregion

    #region ����

    [Tooltip("��������")]
    public float progress = 0;
    [Tooltip("������������")]
    public float progress_Max = 100;
    [Tooltip("�����ٶ�")]
    public float productionSpeed = 1;

    #endregion

    #region ��֪
    [Tooltip("��֪��Χ")]
    public float sightRange = 10;
    #endregion

    #region ���

    [ShowNonSerializedField]
    [Tooltip("�������")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    #region ����
    [Tooltip("��������")]
    public ItemValues ItemDataValue;

    [Tooltip("������Ʒ")]
    public List_Loot loot;
    #endregion

    #region �Ŷ�
    public string TeamID = "";

    public Dictionary<string, RelationType> Relations = new Dictionary<string, RelationType>();
    #endregion

    public override int SyncData()
    {
        int itemRow = base.SyncData();
        var excel = m_ExcelManager.Instance;

        // ͬ��Ѫ��
        hp.maxValue = excel.GetConvertedValue<float>(ExcelIdentifyRow.MaxHp, itemRow, 0.0f);
        hp.Value = excel.GetConvertedValue<float>(ExcelIdentifyRow.Hp, itemRow, 0.0f);

        // ͬ����������
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