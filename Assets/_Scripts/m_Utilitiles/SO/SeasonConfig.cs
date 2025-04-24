using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[CreateAssetMenu(menuName = "Game/Season Config", fileName = "NewSeasonConfig")]
public class SeasonConfig : ScriptableObject
{
    [Tooltip("��������")]
    public SeasonType season;
    [Tooltip("��������")]
    public string seasonName;

    [Tooltip("�˼����е���Ϸ����")]
    public List<DayConfig> days;

    [Tooltip("�ü�����һ�����Ϸʱ�����룩")]
    public float gameDayDuration = 24f * 60f; // Ĭ��24����

    [Tooltip("���ڳ�������Ϸ����")]
    public float durationInDays;

    [Tooltip("�ճ���ʼʱ�䣨�룩")]
    public float sunriseStartTime;
    [Tooltip("�ճ�����ʱ�䣨�룩")]
    public float sunriseEndTime;
    [Tooltip("���俪ʼʱ�䣨�룩")]
    public float sunsetStartTime;
    [Tooltip("�������ʱ�䣨�룩")]
    public float sunsetEndTime;

    public void AutoGenerateParameters()
    {
        Random.InitState((int)season); // ȷ��ͬһ���ڵ������һ��
        days.Clear();

        switch (season)
        {
            case SeasonType.Spring:
                durationInDays = 5;
                GenerateDays(5, 0.2f, 0.3f, 0.7f, 0.8f);
                break;
            case SeasonType.Summer:
                durationInDays = 10;
                GenerateDays(10, 0.15f, 0.25f, 0.75f, 0.85f);
                break;
            case SeasonType.Autumn:
                durationInDays = 5;
                GenerateDays(5, 0.25f, 0.35f, 0.65f, 0.75f);
                break;
            case SeasonType.Winter:
                durationInDays = 10;
                GenerateDays(10, 0.3f, 0.4f, 0.6f, 0.7f);
                break;
        }
    }

    private void GenerateDays(int count, float sunriseStartBase, float sunriseEndBase, float sunsetStartBase, float sunsetEndBase)
    {
        for (int i = 0; i < count; i++)
        {
            var dayConfig = ScriptableObject.CreateInstance<DayConfig>();
            dayConfig.gameDayDuration = gameDayDuration;

            // �������ƫ�ƣ���5%��
            float randomFactor = Random.Range(-0.05f, 0.05f);

            dayConfig.sunriseStartTime = sunriseStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunriseEndTime = sunriseEndBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetStartTime = sunsetStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetEndTime = sunsetEndBase * gameDayDuration * (1 + randomFactor);

            // ȷ��ʱ��˳����ȷ
            dayConfig.sunriseEndTime = Mathf.Max(dayConfig.sunriseStartTime + 0.05f * gameDayDuration, dayConfig.sunriseEndTime);
            dayConfig.sunsetStartTime = Mathf.Max(dayConfig.sunriseEndTime + 0.05f * gameDayDuration, dayConfig.sunsetStartTime);
            dayConfig.sunsetEndTime = Mathf.Min(dayConfig.sunsetStartTime + 0.1f * gameDayDuration, gameDayDuration);

            days.Add(dayConfig);
        }
    }
}

public enum SeasonType
{
    Spring,
    Summer,
    Autumn,
    Winter
}
