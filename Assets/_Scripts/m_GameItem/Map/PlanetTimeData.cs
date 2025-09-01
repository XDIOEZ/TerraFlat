using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class PlanetTimeData
{
    [Header("ʱ�����")]
    public float DayTime = 0;
    public float SeasonTime = 0;
    public float YearTime = 0;
    [Header("�������")]
    public float Day = 0;//1440 ��һ
    public float Season = 0;//8640 ��һ
    public float Year = 0; //34560 ��һ

    [Header("ʱ�䳣��")]
    [Tooltip("һ��ʱ��")]
    public float OneDayTime = 1440;//һ��1440�� 60*24 ��λ�� 1����60�� 24����
    [Tooltip("һ��ʱ��")]
    public float OneSeasonTime = 8640;//һ��8640��  6��Ϊһ�� 24*6*60 ��λ�� 1��60���� 6��24Сʱ
    [Tooltip("һ��ʱ��")]
    public float OneYearTime = 34560;//һ��31536�� 

    [Tooltip("��ת�ٶ�")]
    public float RotationSpeed = 1f;
    [Tooltip("��ת�ٶ�")]
    public float OrbitSpeed = 1f;

    [Header("������ز���")]
    [Tooltip("����-�ճ�������ֵ")]//�����ҹ1:1
    public Vector2 SunriseSunsetThreshold_Spring = new Vector2(0.3f, 0.2f);
    [Tooltip("�ļ�-�ճ�������ֵ")]//�����ҹ1.5:1
    public Vector2 SunriseSunsetThreshold_Summer = new Vector2(0.2f, 0.1f);
    [Tooltip("�＾-�ճ�������ֵ")]
    public Vector2 SunriseSunsetThreshold_Autumn = new Vector2(0.3f, 0.2f);
    [Tooltip("����-�ճ�������ֵ")]
    public Vector2 SunriseSunsetThreshold_Winter = new Vector2(0.5f, 0.4f);
}