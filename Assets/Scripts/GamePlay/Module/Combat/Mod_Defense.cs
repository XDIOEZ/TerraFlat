using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Defense : Module
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; }  set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    public GameValue_float Defense = new GameValue_float(0);
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
        if (item.itemMods.ContainsKey_ID(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.Data.Defense += Defense;
        }
    }

    public override void Save()
    {
        // 取消Load中的加成
        if (item.Mods.ContainsKey(ModText.Hp))
        {
            var Hp = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            Hp.Data.Defense -= Defense;
        }
        SaveData.WriteData(Defense);
    }
}
