using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_MoveSpeed : Module
{
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }

    public GameValue_float MoveSpeed = new GameValue_float(1.2f); // Ĭ�ϼ��ٱ��ʣ�����1.2��

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.MoveSpeed; // ȷ�� ModText ������ֶ�
        }
    }

    public override void Load()
    {
        SaveData.ReadData(ref MoveSpeed);

        if (item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            var movement = item.itemMods.GetMod_ByID(ModText.Mover) as Mover; // ��������ƶ�������� MovementController
            movement.Data.Speed.MultiplicativeModifier *= MoveSpeed.Value;
        }
    }

    public override void Save()
    {
        // ��ԭ�ӳ�
        if (item.itemMods.ContainsKey_ID(ModText.Mover))
        {
            var movement = item.itemMods.GetMod_ByID(ModText.Mover) as Mover; // ��������ƶ�������� MovementController
            movement.Data.Speed.MultiplicativeModifier /= MoveSpeed.Value;
        }

        SaveData.WriteData(MoveSpeed);
    }
}
