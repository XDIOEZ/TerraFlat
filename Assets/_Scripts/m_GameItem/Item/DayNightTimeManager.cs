using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using NaughtyAttributes;

public class DayNightTimeManager : SingletonMono<DayNightTimeManager>
{
    #region 变量声明
    // 时间相关字段
    #region 时间设置
    [Header("时间设置")]
    [Tooltip("当前一天已过去的时间（秒）")]
    public float currentDayTime = 0f;
    #endregion

    #region 时间流逝控制
    [Header("时间流逝控制")]
    [Tooltip("时间流逝速度倍数，默认为1，可通过此参数加速或减缓时间")]
    public float timeScale = 1f;
    #endregion

    // 光照相关字段
    #region 光照设置
    [Header("全局光照设置")]
    [Tooltip("场景中控制光照的Light2D组件")]
    public Light2D globalLight;
    [Tooltip("全局光照的最大强度值")]
    public float maxLightIntensity = 1f;
    [Tooltip("全局光照的最小强度值")]
    public float minLightIntensity = 0f;
    #endregion

    // 日期和季节相关字段
    #region 日期设置
    [Header("日期设置")]
    [Tooltip("当前年份，默认值为2025年")]
    public int year = 2025;
    [Tooltip("当前天数，从1开始计数")]
    public int day = 1;
    #endregion

    #region 季节设置
    [Header("季节设置")]
    [SerializeField]
    [Tooltip("所有季节配置的列表")]
    private List<SeasonConfig> seasonConfigs;

    [Tooltip("当前所处的季节类型")]
    public SeasonType currentSeason;

    [Tooltip("当前季节已过去的天数计数器")]
    public float seasonDayCounter = 0f;
    [Tooltip("当前季节一天的持续时间（秒）")]
    public float currentDayDuration;

    [SerializeField, Tooltip("当前生效的季节配置")]
    public SeasonConfig currentSeasonConfig;
    [Tooltip("当前季节在days列表中的索引")]
    public int currentSeasonDayIndex;
    #endregion

    #region 季节统计信息
    [ReadOnly, Tooltip("当前季节的总天数")]
    public int currentSeasonTotalDays;
    [ReadOnly, Tooltip("当前季节剩余天数")]
    public int currentSeasonRemainingDays;
    #endregion

    #region 日出日落时间点
    [Tooltip("日出开始时间（秒）")]
    public float sunriseStartTime;
    [Tooltip("日出结束时间（秒）")]
    public float sunriseEndTime;
    [Tooltip("日落开始时间（秒）")]
    public float sunsetStartTime;
    [Tooltip("日落结束时间（秒）")]
    public float sunsetEndTime;
    #endregion

    // 特殊日子相关字段
    #region 特殊日子设置
    [Header("特殊日子设置")]
    [SerializeField]
    [Tooltip("特殊日子的配置列表")]
    private List<DayConfig> specialDayConfigs;

    public bool isSpecialDayActive = false;
    [Tooltip("当前生效的特殊日子配置")]
    public DayConfig currentSpecialDayConfig;
    [ Tooltip("是否处于特殊日子状态")]
    public bool IsSpecialDayActive => isSpecialDayActive;
    #endregion

    #region 季节切换记录
    [Tooltip("切换季节前的上一个季节配置")]
    public SeasonConfig previousSeasonConfig;
    [Tooltip("切换季节前的上一个季节天数索引")]
    public int previousSeasonDayIndex;
    #endregion
    #endregion

    #region 初始化方法
    void Start()
    {
        if (seasonConfigs == null || seasonConfigs.Count == 0)
        {
            Debug.LogError("未设置 SeasonConfig 资产，请先创建并分配到列表中！");
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

    #region 更新逻辑
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

    #region 核心功能方法
    // 更新当前天配置
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

    // 更新光照强度
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

    // 切换到下一天
    [Button("下一天")]
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

    #region 配置应用方法
    // 应用特殊日子配置
    private void ApplySpecialDayConfig(DayConfig config)
    {
        currentDayDuration = config.gameDayDuration;
        sunriseStartTime = config.sunriseStartTime;
        sunriseEndTime = config.sunriseEndTime;
        sunsetStartTime = config.sunsetStartTime;
        sunsetEndTime = config.sunsetEndTime;
    }

    // 应用季节配置
    private void ApplySeasonConfig(SeasonConfig config)
    {
        currentSeasonConfig = config;
        currentSeasonDayIndex = 0;
        currentSeasonTotalDays = config.days.Count;
        currentSeasonRemainingDays = currentSeasonTotalDays;
        UpdateCurrentDayConfig();
    }
    #endregion

    #region 工具方法
    // 获取当前季节配置
    private SeasonConfig GetCurrentSeasonConfig()
    {
        return seasonConfigs.Find(s => s.season == currentSeason);
    }

    // 调试字符串
    public override string ToString()
    {
        return $"Year {year}, Day {day} | {currentSeason}剩余{currentSeasonRemainingDays}/{currentSeasonTotalDays}天";
    }
    #endregion
}