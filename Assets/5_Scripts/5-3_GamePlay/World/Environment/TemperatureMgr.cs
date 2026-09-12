using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>统一逐格环境温度查询与角色体温结算；局部冷热源由空间缓存独立维护，角色每 0.25 秒采样一次。</summary>
public partial class TemperatureMgr : SingletonAutoMono<TemperatureMgr>
{
#region 字段

    public const float DefaultAmbientTemperature = 20f; // 默认环境温度
    public const float DamageTickIntervalSeconds = 5f; // 温度伤害结算间隔

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

        data.ChangeSpeed = Mathf.Max(0f, data.ChangeSpeed);

        data.ColdDamagePerSecond = Mathf.Max(0f, data.ColdDamagePerSecond);
        data.HotDamagePerSecond = Mathf.Max(0f, data.HotDamagePerSecond);

        data.HotDamageStart = Mathf.Max(data.ColdDamageStart, data.HotDamageStart);
        if (data.RuntimeChangeSpeedMultiplier <= 0f)
            data.RuntimeChangeSpeedMultiplier = 1f;
        if (data.RuntimeCoolingSpeedMultiplier <= 0f)
            data.RuntimeCoolingSpeedMultiplier = 1f;
    }

    /// <summary>推进体温并结算温度伤害；返回本次是否实际造成了低温伤害。</summary>
    public bool ProcessTemperature(
        Mod_Temperature.TemperatureData data,
        DamageReceiver damageReceiver,
        float deltaTime,
        Action<float> onTemperatureChanged,
        ref float damageTickTimer,
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

        if (damageReceiver == null)
        {
            return false;
        }

        damageTickTimer += deltaTime;
        if (damageTickTimer < DamageTickIntervalSeconds)
        {
            return false;
        }

        int tickCount = Mathf.FloorToInt(damageTickTimer / DamageTickIntervalSeconds);
        damageTickTimer -= tickCount * DamageTickIntervalSeconds;

        float damage = 0f;
        for (int i = 0; i < tickCount; i++)
        {
            damage += EvaluateTemperatureDamage(data, DamageTickIntervalSeconds);
        }

        if (damage <= 0f)
        {
            return false;
        }

        bool coldDamageApplied = data.CurrentTemperature < data.ColdDamageStart;
        damageReceiver.ForceHurt(damage);

        if (EnableDebugLog)
        {
            Debug.Log($"[TemperatureMgr] 触发温度伤害，当前体温={data.CurrentTemperature:F2}℃，结算次数={tickCount}，伤害={damage:F3}");
        }

        return coldDamageApplied;
    }

    public float EvaluateNextTemperature(Mod_Temperature.TemperatureData data, float deltaTime,
        float? naturalTemperature = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        float targetTemperature =
            data.AmbientTemperature + data.Insulation + data.RuntimeAmbientOffset;
        // 临时 Buff 增温不参与环境趋近计算，伤害仍读取回调提交后的有效体温。
        float currentTemperature = naturalTemperature ?? data.CurrentTemperature;
        float changeSpeed = data.ChangeSpeed * Mathf.Max(0f, data.RuntimeChangeSpeedMultiplier);
        if (targetTemperature < currentTemperature)
            changeSpeed *= Mathf.Max(0f, data.RuntimeCoolingSpeedMultiplier);

        return Mathf.MoveTowards(currentTemperature, targetTemperature, changeSpeed * deltaTime);
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

    public float EvaluateTemperatureDamage(Mod_Temperature.TemperatureData data, float deltaTime)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        float coldDamage = 0f;
        if (data.CurrentTemperature < data.ColdDamageStart)
        {
            float ratio = Mathf.Max(0f, data.ColdDamageStart - data.CurrentTemperature);
            coldDamage = ratio * data.ColdDamagePerSecond * deltaTime;
        }

        float hotDamage = 0f;
        if (data.CurrentTemperature > data.HotDamageStart)
        {
            float ratio = Mathf.Max(0f, data.CurrentTemperature - data.HotDamageStart);
            hotDamage = ratio * data.HotDamagePerSecond * deltaTime;
        }

        return coldDamage + hotDamage;
    }

#endregion
}
