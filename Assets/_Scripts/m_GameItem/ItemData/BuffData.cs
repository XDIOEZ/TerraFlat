using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "�µ�Buff����", menuName = "ScriptableObjects/�½�Buff����")]
public class Buff_Data : ScriptableObject
{
    //Buff��ID
    public string buff_ID;
    //Buff������
    public string buff_Name; 
    //Buff������
    public string buff_Type; 
    //Buff������
    public string buff_Description;

    //Buff�ĳ���ʱ��
    public float buff_Duration = 5f;
    //buff��ִ�м��
    public float buff_Interval = 0f;
    //buff�����ѵ�����
    public int buff_MaxStack = 1;
    //buff�Ķѵ�����
    public BuffStackType buff_StackType;

    public BuffAction buff_Behavior_Start;
    public BuffAction buff_Behavior_Update;
    public BuffAction buff_Behavior_Stop;
}
public enum BuffStackType
{
    DurationAdd,       // ��ε��ӳ���ʱ��
    RefreshDuration,   // ÿ��ˢ�³���ʱ��
    StackCount,         // ���Ӳ���
        Keep
}


