using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Defense : Module
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; }  set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    [Header("四类防御加成")]
    public CombatDefense DefenseValues = new CombatDefense();

    [HideInInspector]
    public GameValue_float Defense = new GameValue_float(0);

    [SerializeField, HideInInspector]
    private int damageSystemVersion;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Defense;
        }

    }


    public override void Load()
    {
        SaveData.ReadData(ref Defense);
        UpgradeLegacyDefense();
        if (item.itemMods.ContainsKey_ID(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.AddDefense(DefenseValues);
        }
    }

    public override void Save()
    {
        // 取消Load中的加成
        if (item.Mods.ContainsKey(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.RemoveDefense(DefenseValues);
        }
        // 保持旧存档载荷可读；四类基础防御由模块配置负责。
        SaveData.WriteData(Defense);
    }

    /// <summary>旧防御模块的单值加成迁为四类相同加成。</summary>
    private void UpgradeLegacyDefense()
    {
        DefenseValues ??= new CombatDefense();
        DefenseValues.ClampNonNegative();
        if (damageSystemVersion >= 1)
            return;

        if (DefenseValues.TotalDefense <= 0f && Defense != null && Defense.Value > 0f)
        {
            float value = Mathf.Max(0f, Defense.Value);
            DefenseValues = new CombatDefense(value, value, value, value);
        }

        damageSystemVersion = 1;
    }

}
