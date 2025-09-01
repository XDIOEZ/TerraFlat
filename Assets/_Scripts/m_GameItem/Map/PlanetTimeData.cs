using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class PlanetTimeData
{
    [Header("时间相关")]
    public float DayTime = 0;
    public float SeasonTime = 0;
    public float YearTime = 0;
    [Header("计数相关")]
    public float Day = 0;//1440 进一
    public float Season = 0;//8640 进一
    public float Year = 0; //34560 进一

    [Header("时间常数")]
    [Tooltip("一天时间")]
    public float OneDayTime = 1440;//一天1440秒 60*24 单位秒 1分钟60秒 24分钟
    [Tooltip("一季时间")]
    public float OneSeasonTime = 8640;//一季8640秒  6天为一季 24*6*60 单位秒 1天60分钟 6天24小时
    [Tooltip("一年时间")]
    public float OneYearTime = 34560;//一年31536秒 

    [Tooltip("自转速度")]
    public float RotationSpeed = 1f;
    [Tooltip("公转速度")]
    public float OrbitSpeed = 1f;

    [Header("季节相关参数")]
    [Tooltip("春季-日出日落阈值")]//白天黑夜1:1
    public Vector2 SunriseSunsetThreshold_Spring = new Vector2(0.3f, 0.2f);
    [Tooltip("夏季-日出日落阈值")]//白天黑夜1.5:1
    public Vector2 SunriseSunsetThreshold_Summer = new Vector2(0.2f, 0.1f);
    [Tooltip("秋季-日出日落阈值")]
    public Vector2 SunriseSunsetThreshold_Autumn = new Vector2(0.3f, 0.2f);
    [Tooltip("冬季-日出日落阈值")]
    public Vector2 SunriseSunsetThreshold_Winter = new Vector2(0.5f, 0.4f);
}