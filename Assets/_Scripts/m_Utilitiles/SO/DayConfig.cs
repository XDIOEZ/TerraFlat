using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Day Config", fileName = "NewDayConfig")]
public class DayConfig : ScriptableObject
{
    public string dayName;
    public int year;
    public int day;

    [Tooltip("该天的游戏时长（秒）")]
    public float gameDayDuration = 24f * 60f; // 默认24分钟

    [Tooltip("日出开始时间（秒）")]
    public float sunriseStartTime;
    [Tooltip("日出结束时间（秒）")]
    public float sunriseEndTime;
    [Tooltip("日落开始时间（秒）")]
    public float sunsetStartTime;
    [Tooltip("日落结束时间（秒）")]
    public float sunsetEndTime;
}
