using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class BuffData:ItemData
{
    public string buff_Name; //Buff Name
    public float buff_Duration; //Buff ����ʱ��
    public float buff_Value; //Buff Ч��ֵ
    //Buff������
    public string buff_Description;
    //Buff�����ö����ʶ����
    public List<string> buff_TargetID;

}
