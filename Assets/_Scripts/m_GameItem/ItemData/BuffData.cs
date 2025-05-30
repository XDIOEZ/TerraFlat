using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class BuffData
{
    public string buff_Name; //Buff Name
    public string buff_Type; //Buff Type
    public string buff_ID; //Buff ID
    public string buff_Description;

    public float buff_CurrentDuration = 0f;
    public float buff_MaxDuration = 5f;

    public float buff_Value = 0.5f; // ͨ����ֵ�ֶΣ����磺���ٱ��ʡ���Ѫ��ֵ��
}
