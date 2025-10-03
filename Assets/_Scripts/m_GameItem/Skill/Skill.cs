using UnityEngine;
using System.Collections;

public class Skill : Module
{
    #region ��������

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public float Data;
    #endregion
    #region ģ�����

    public BaseSkill skillData;

    #endregion

    #region ��������

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref Data);
    }
    public override void ModUpdate(float deltaTime)
    {

    }
    public override void Save()
    {
        ModSaveData.WriteData(Data);
    }
    public override void Act()
    {
        base.Act();
    }
    #endregion


}