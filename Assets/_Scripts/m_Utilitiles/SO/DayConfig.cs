using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Day Config", fileName = "NewDayConfig")]
public class DayConfig : ScriptableObject
{
    public string dayName;
    public int year;
    public int day;

    [Tooltip("�������Ϸʱ�����룩")]
    public float gameDayDuration = 24f * 60f; // Ĭ��24����

    [Tooltip("�ճ���ʼʱ�䣨�룩")]
    public float sunriseStartTime;
    [Tooltip("�ճ�����ʱ�䣨�룩")]
    public float sunriseEndTime;
    [Tooltip("���俪ʼʱ�䣨�룩")]
    public float sunsetStartTime;
    [Tooltip("�������ʱ�䣨�룩")]
    public float sunsetEndTime;
}
