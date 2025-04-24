using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using NaughtyAttributes;

public class DayNightTimeManager : SingletonMono<DayNightTimeManager>
{
    #region ��������
    // ʱ������ֶ�
    #region ʱ������
    [Header("ʱ������")]
    [Tooltip("��ǰһ���ѹ�ȥ��ʱ�䣨�룩")]
    public float currentDayTime = 0f;
    #endregion

    #region ʱ�����ſ���
    [Header("ʱ�����ſ���")]
    [Tooltip("ʱ�������ٶȱ�����Ĭ��Ϊ1����ͨ���˲������ٻ����ʱ��")]
    public float timeScale = 1f;
    #endregion

    // ��������ֶ�
    #region ��������
    [Header("ȫ�ֹ�������")]
    [Tooltip("�����п��ƹ��յ�Light2D���")]
    public Light2D globalLight;
    [Tooltip("ȫ�ֹ��յ����ǿ��ֵ")]
    public float maxLightIntensity = 1f;
    [Tooltip("ȫ�ֹ��յ���Сǿ��ֵ")]
    public float minLightIntensity = 0f;
    #endregion

    // ���ںͼ�������ֶ�
    #region ��������
    [Header("��������")]
    [Tooltip("��ǰ��ݣ�Ĭ��ֵΪ2025��")]
    public int year = 2025;
    [Tooltip("��ǰ��������1��ʼ����")]
    public int day = 1;
    #endregion

    #region ��������
    [Header("��������")]
    [SerializeField]
    [Tooltip("���м������õ��б�")]
    private List<SeasonConfig> seasonConfigs;

    [Tooltip("��ǰ�����ļ�������")]
    public SeasonType currentSeason;

    [Tooltip("��ǰ�����ѹ�ȥ������������")]
    public float seasonDayCounter = 0f;
    [Tooltip("��ǰ����һ��ĳ���ʱ�䣨�룩")]
    public float currentDayDuration;

    [SerializeField, Tooltip("��ǰ��Ч�ļ�������")]
    public SeasonConfig currentSeasonConfig;
    [Tooltip("��ǰ������days�б��е�����")]
    public int currentSeasonDayIndex;
    #endregion

    #region ����ͳ����Ϣ
    [ReadOnly, Tooltip("��ǰ���ڵ�������")]
    public int currentSeasonTotalDays;
    [ReadOnly, Tooltip("��ǰ����ʣ������")]
    public int currentSeasonRemainingDays;
    #endregion

    #region �ճ�����ʱ���
    [Tooltip("�ճ���ʼʱ�䣨�룩")]
    public float sunriseStartTime;
    [Tooltip("�ճ�����ʱ�䣨�룩")]
    public float sunriseEndTime;
    [Tooltip("���俪ʼʱ�䣨�룩")]
    public float sunsetStartTime;
    [Tooltip("�������ʱ�䣨�룩")]
    public float sunsetEndTime;
    #endregion

    // ������������ֶ�
    #region ������������
    [Header("������������")]
    [SerializeField]
    [Tooltip("�������ӵ������б�")]
    private List<DayConfig> specialDayConfigs;

    public bool isSpecialDayActive = false;
    [Tooltip("��ǰ��Ч��������������")]
    public DayConfig currentSpecialDayConfig;
    [ Tooltip("�Ƿ�����������״̬")]
    public bool IsSpecialDayActive => isSpecialDayActive;
    #endregion

    #region �����л���¼
    [Tooltip("�л�����ǰ����һ����������")]
    public SeasonConfig previousSeasonConfig;
    [Tooltip("�л�����ǰ����һ��������������")]
    public int previousSeasonDayIndex;
    #endregion
    #endregion

    #region ��ʼ������
    void Start()
    {
        if (seasonConfigs == null || seasonConfigs.Count == 0)
        {
            Debug.LogError("δ���� SeasonConfig �ʲ������ȴ��������䵽�б��У�");
            return;
        }

        currentSeason = seasonConfigs[0].season;
        ApplySeasonConfig(GetCurrentSeasonConfig());

        currentSeasonTotalDays = (int)GetCurrentSeasonConfig().durationInDays;
        currentSeasonRemainingDays = currentSeasonTotalDays;

        currentSeasonConfig = seasonConfigs[0];
        currentSeason = currentSeasonConfig.season;
        currentSeasonDayIndex = 0;
        currentSeasonTotalDays = currentSeasonConfig.days.Count;
        currentSeasonRemainingDays = currentSeasonTotalDays;
        UpdateCurrentDayConfig();
    }
    #endregion

    #region �����߼�
    void Update()
    {
        currentDayTime += Time.deltaTime * timeScale;
        if (currentDayTime >= currentDayDuration)
        {
            currentDayTime -= currentDayDuration;
            NextDay();
        }

        UpdateLightIntensity();
    }
    #endregion

    #region ���Ĺ��ܷ���
    // ���µ�ǰ������
    private void UpdateCurrentDayConfig()
    {
        if (currentSeasonConfig != null && currentSeasonDayIndex < currentSeasonConfig.days.Count)
        {
            var dayConfig = currentSeasonConfig.days[currentSeasonDayIndex];
            currentDayDuration = dayConfig.gameDayDuration;
            sunriseStartTime = dayConfig.sunriseStartTime;
            sunriseEndTime = dayConfig.sunriseEndTime;
            sunsetStartTime = dayConfig.sunsetStartTime;
            sunsetEndTime = dayConfig.sunsetEndTime;
        }
    }

    // ���¹���ǿ��
    private void UpdateLightIntensity()
    {
        SeasonConfig config = GetCurrentSeasonConfig();
        if (config == null) return;

        if (currentDayTime >= sunriseStartTime && currentDayTime <= sunriseEndTime)
        {
            float t = (currentDayTime - sunriseStartTime) / (sunriseEndTime - sunriseStartTime);
            globalLight.intensity = Mathf.Lerp(minLightIntensity, maxLightIntensity, t);
        }
        else if (currentDayTime > sunriseEndTime && currentDayTime < sunsetStartTime)
        {
            globalLight.intensity = maxLightIntensity;
        }
        else if (currentDayTime >= sunsetStartTime && currentDayTime <= sunsetEndTime)
        {
            float t = (currentDayTime - sunsetStartTime) / (sunsetEndTime - sunsetStartTime);
            globalLight.intensity = Mathf.Lerp(maxLightIntensity, minLightIntensity, t);
        }
        else
        {
            globalLight.intensity = minLightIntensity;
        }
    }

    // �л�����һ��
    [Button("��һ��")]
    private void NextDay()
    {
        if (isSpecialDayActive)
        {
            currentSeasonConfig = previousSeasonConfig;
            currentSeasonDayIndex = previousSeasonDayIndex;
            UpdateCurrentDayConfig();
            isSpecialDayActive = false;
        }

        day++;
        seasonDayCounter++;

        currentSpecialDayConfig = specialDayConfigs.Find(config => config.year == year && config.day == day);
        if (currentSpecialDayConfig != null)
        {
            previousSeasonConfig = currentSeasonConfig;
            previousSeasonDayIndex = currentSeasonDayIndex;
            ApplySpecialDayConfig(currentSpecialDayConfig);
            isSpecialDayActive = true;
        }
        else
        {
            currentSeasonDayIndex++;
            if (currentSeasonDayIndex >= currentSeasonConfig.days.Count)
            {
                int currentIndex = seasonConfigs.FindIndex(s => s == currentSeasonConfig);
                currentIndex = (currentIndex + 1) % seasonConfigs.Count;
                currentSeasonConfig = seasonConfigs[currentIndex];
                currentSeason = currentSeasonConfig.season;
                currentSeasonDayIndex = 0;
                currentSeasonTotalDays = currentSeasonConfig.days.Count;
                currentSeasonRemainingDays = currentSeasonTotalDays;
                UpdateCurrentDayConfig();
            }
            else
            {
                UpdateCurrentDayConfig();
            }
        }

        currentSeasonRemainingDays = currentSeasonTotalDays - currentSeasonDayIndex;

        int totalDaysInYear = 0;
        foreach (var season in seasonConfigs)
        {
            totalDaysInYear += season.days.Count;
        }
        if (day > totalDaysInYear)
        {
            day = 1;
            year++;
        }
    }
    #endregion

    #region ����Ӧ�÷���
    // Ӧ��������������
    private void ApplySpecialDayConfig(DayConfig config)
    {
        currentDayDuration = config.gameDayDuration;
        sunriseStartTime = config.sunriseStartTime;
        sunriseEndTime = config.sunriseEndTime;
        sunsetStartTime = config.sunsetStartTime;
        sunsetEndTime = config.sunsetEndTime;
    }

    // Ӧ�ü�������
    private void ApplySeasonConfig(SeasonConfig config)
    {
        currentSeasonConfig = config;
        currentSeasonDayIndex = 0;
        currentSeasonTotalDays = config.days.Count;
        currentSeasonRemainingDays = currentSeasonTotalDays;
        UpdateCurrentDayConfig();
    }
    #endregion

    #region ���߷���
    // ��ȡ��ǰ��������
    private SeasonConfig GetCurrentSeasonConfig()
    {
        return seasonConfigs.Find(s => s.season == currentSeason);
    }

    // �����ַ���
    public override string ToString()
    {
        return $"Year {year}, Day {day} | {currentSeason}ʣ��{currentSeasonRemainingDays}/{currentSeasonTotalDays}��";
    }
    #endregion
}