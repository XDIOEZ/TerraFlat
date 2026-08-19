using UnityEngine;

public partial class Mod_Defense : Module
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; }  set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    [Header("四类防御加成")]
    public CombatDefense DefenseValues = new CombatDefense();

    /// <summary>初始化防御模块的默认数据标识。</summary>
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Defense;
        }

    }


    /// <summary>校正四类防御并挂接到生命模块。</summary>
    public override void Load()
    {
        DefenseValues ??= new CombatDefense();
        DefenseValues.ClampNonNegative();
        if (item.itemMods.ContainsKey_ID(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.AddDefense(DefenseValues);
        }
    }

    /// <summary>卸载本模块对生命模块施加的四类防御。</summary>
    public override void Save()
    {
        // 取消Load中的加成
        if (item.Mods.ContainsKey(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.RemoveDefense(DefenseValues);
        }
    }

}
