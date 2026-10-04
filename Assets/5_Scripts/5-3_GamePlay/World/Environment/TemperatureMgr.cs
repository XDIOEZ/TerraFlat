using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>统一逐格环境温度查询与角色体表温度结算；局部冷热源由空间缓存独立维护，角色每 0.25 秒采样一次。</summary>
public partial class TemperatureMgr : SingletonAutoMono<TemperatureMgr>
{
#region 字段

    public const float DefaultAmbientTemperature = 20f; // 默认环境温度
    public bool EnableDebugLog = false; // 是否输出温度处理调试日志

#endregion

#region 属性

    [ShowInInspector, ReadOnly, LabelText("当前全局温度(℃)")]
    public float CurrentGlobalAmbientTemperature => GetGlobalAmbientTemperature(); // 检查器显示的全局环境温度

#endregion

#region 公共方法

    public void NormalizeData(Mod_Temperature.TemperatureData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        data.SafeTemperatureMax = Mathf.Max(data.SafeTemperatureMin, data.SafeTemperatureMax);
        if (!float.IsFinite(data.DangerTemperatureBufferSeconds) || data.DangerTemperatureBufferSeconds < 0f ||
            !float.IsFinite(data.TemperatureBufferRecoveryPerSecond) || data.TemperatureBufferRecoveryPerSecond < 0f ||
            !float.IsFinite(data.RemainingTemperatureBufferSeconds))
            throw new ArgumentOutOfRangeException(nameof(data), "危险温度缓冲参数必须是有限非负数，剩余时间必须是有限数值。");
        data.RemainingTemperatureBufferSeconds = Mathf.Clamp(
            data.RemainingTemperatureBufferSeconds, 0f, data.DangerTemperatureBufferSeconds);
        if (data.RuntimeChangeSpeedMultiplier <= 0f)
            data.RuntimeChangeSpeedMultiplier = 1f;
        if (data.RuntimeCoolingSpeedMultiplier <= 0f)
            data.RuntimeCoolingSpeedMultiplier = 1f;
    }

    /// <summary>推进角色体表温度；超出安全范围后的状态与伤害由 Buff 系统统一结算。</summary>
    public void ProcessTemperature(
        Mod_Temperature.TemperatureData data,
        float deltaTime,
        Action<float> onTemperatureChanged,
        float? naturalTemperature = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (onTemperatureChanged == null)
        {
            throw new ArgumentNullException(nameof(onTemperatureChanged));
        }

        float nextTemperature = EvaluateNextTemperature(data, deltaTime, naturalTemperature);
        onTemperatureChanged(nextTemperature);
    }

    public float EvaluateNextTemperature(Mod_Temperature.TemperatureData data, float deltaTime,
        float? naturalTemperature = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        float targetTemperature = data.AmbientTemperature + data.RuntimeAmbientOffset;
        // 临时 Buff 增温不参与环境趋近计算，伤害仍读取回调提交后的有效体温。
        float currentTemperature = naturalTemperature ?? data.CurrentTemperature;
        float directionMultiplier = targetTemperature < currentTemperature
            ? data.RuntimeCoolingSpeedMultiplier
            : 1f;
        float transferPerSecond = Mathf.Max(
            1f,
            Mathf.Floor(Mathf.Abs(targetTemperature) * 0.01f + 0.5f));
        return ThermalRuntime.AdvanceTowards(
            currentTemperature,
            targetTemperature,
            transferPerSecond,
            deltaTime,
            Mathf.Max(0f, data.RuntimeChangeSpeedMultiplier) * Mathf.Max(0f, directionMultiplier));
    }

    public float GetGlobalAmbientTemperature()
    {
        PlanetData planetData = SaveDataMgr.Instance != null ? SaveDataMgr.Instance.Active_PlanetData : null;
        if (planetData == null)
        {
            return DefaultAmbientTemperature;
        }

        float weatherOffset = DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather == true
            ? 0f : WeatherMgr.CalculateWeatherTemperatureOffset(planetData);
        return planetData.GlobalTemperature + weatherOffset + DayTimeSystem.GetSeasonTemperatureOffset();
    }

    public void SetGlobalAmbientTemperature(float value)
    {
        PlanetData planetData = SaveDataMgr.Instance != null ? SaveDataMgr.Instance.Active_PlanetData : null;
        if (planetData == null)
        {
            if (EnableDebugLog)
            {
                Debug.LogWarning($"[TemperatureMgr] 设置全局温度失败，未找到当前星球数据，目标温度={value:F1}℃");
            }

            return;
        }

        planetData.GlobalTemperature = value;
        fieldContextFrame = -1;

        if (EnableDebugLog)
        {
            Debug.Log($"[TemperatureMgr] 设置基础环境温度成功，基础温度={value:F1}℃，天气修正={WeatherMgr.CalculateWeatherTemperatureOffset(planetData):F1}℃，有效环境温度={GetGlobalAmbientTemperature():F1}℃");
        }
    }

#endregion
}

/// <summary>统一处理生物、物质与液体按传热速率向目标温度趋近的基础计算。</summary>
public static class ThermalRuntime
{
#region 温度推进

    /// <summary>按每秒传热速率推进当前温度；速率、倍率与时间均不允许产生反向传热。</summary>
    public static float AdvanceTowards(
        float currentTemperature,
        float targetTemperature,
        float transferPerSecond,
        float seconds,
        float transferMultiplier = 1f)
    {
        float speed = Mathf.Max(0f, transferPerSecond) * Mathf.Max(0f, transferMultiplier);
        return Mathf.MoveTowards(currentTemperature, targetTemperature, speed * Mathf.Max(0f, seconds));
    }

#endregion
}
