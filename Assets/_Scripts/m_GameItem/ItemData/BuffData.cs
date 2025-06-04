using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "新的Buff配置", menuName = "ScriptableObjects/新建Buff配置")]
public class Buff_Data : ScriptableObject
{
    //Buff的ID
    public string buff_ID;
    //Buff的名字
    public string buff_Name; 
    //Buff的类型
    public string buff_Type; 
    //Buff的描述
    public string buff_Description;

    //Buff的持续时间
    public float buff_Duration = 5f;
    //buff的执行间隔
    public float buff_Interval = 0f;
    //buff的最大堆叠数量
    public int buff_MaxStack = 1;
    //buff的堆叠类型
    public BuffStackType buff_StackType;

    public BuffAction buff_Behavior_Start;
    public BuffAction buff_Behavior_Update;
    public BuffAction buff_Behavior_Stop;
}
public enum BuffStackType
{
    DurationAdd,       // 多次叠加持续时间
    RefreshDuration,   // 每次刷新持续时间
    StackCount,         // 增加层数
        Keep
}


