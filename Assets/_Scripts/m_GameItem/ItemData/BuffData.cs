using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class BuffData:ItemData
{
    public string buff_Name; //Buff Name
    public float buff_Duration; //Buff 持续时间
    public float buff_Value; //Buff 效果值
    //Buff的描述
    public string buff_Description;
    //Buff的作用对象的识别码
    public List<string> buff_TargetID;

}
