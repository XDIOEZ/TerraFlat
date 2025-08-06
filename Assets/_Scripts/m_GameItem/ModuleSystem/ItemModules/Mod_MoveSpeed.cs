using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_MoveSpeed : Module
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    public GameValue_float MoveSpeed = new GameValue_float(1.2f); // 默认加速倍率，例如1.2倍

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.MoveSpeed; // 确保 ModText 有这个字段
        }
    }

    public override void Load()
    {
        SaveData.ReadData(ref MoveSpeed);

        if (item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            var movement = item.itemMods.GetMod_ByID(ModText.Mover) as Mover; // 假设控制移动的组件叫 MovementController
            movement.Data.Speed.MultiplicativeModifier *= MoveSpeed.Value;
        }
    }

    public override void Save()
    {
        // 还原加成
        if (item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            var movement = item.itemMods.GetMod_ByID(ModText.Mover) as Mover; // 假设控制移动的组件叫 MovementController
            movement.Data.Speed.MultiplicativeModifier /= MoveSpeed.Value;
        }

        SaveData.WriteData(MoveSpeed);
    }
}
