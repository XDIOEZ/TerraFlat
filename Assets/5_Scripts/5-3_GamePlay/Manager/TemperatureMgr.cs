using System;
using Sirenix.OdinInspector;
using UnityEngine;

public class TemperatureMgr : SingletonAutoMono<TemperatureMgr>
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
    }

    public void ProcessTemperature(
        Mod_Temperature.TemperatureData data,
        DamageReceiver damageReceiver,
        float deltaTime,
        Action<float> onTemperatureChanged,
        ref float damageTickTimer)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (onTemperatureChanged == null)
        {
            throw new ArgumentNullException(nameof(onTemperatureChanged));
        }

        float nextTemperature = EvaluateNextTemperature(data, deltaTime);
        onTemperatureChanged(nextTemperature);

        if (damageReceiver == null)
        {
            return;
        }

        damageTickTimer += deltaTime;
        if (damageTickTimer < DamageTickIntervalSeconds)
        {
            return;
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
            return;
        }

        damageReceiver.ForceHurt(damage);

        if (EnableDebugLog)
        {
            Debug.Log($"[TemperatureMgr] 触发温度伤害，当前体温={data.CurrentTemperature:F2}℃，结算次数={tickCount}，伤害={damage:F3}");
        }
    }

    public float EvaluateNextTemperature(Mod_Temperature.TemperatureData data, float deltaTime)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        float targetTemperature = GetGlobalAmbientTemperature() + data.Insulation;
        return Mathf.MoveTowards(data.CurrentTemperature, targetTemperature, data.ChangeSpeed * deltaTime);
    }

    public float GetGlobalAmbientTemperature()
    {
        PlanetData planetData = SaveDataMgr.Instance != null ? SaveDataMgr.Instance.Active_PlanetData : null;
        if (planetData == null)
        {
            return DefaultAmbientTemperature;
        }

        return planetData.GlobalTemperature + GetWeatherTemperatureOffset(planetData);
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

        if (EnableDebugLog)
        {
            Debug.Log($"[TemperatureMgr] 设置基础环境温度成功，基础温度={value:F1}℃，天气修正={GetWeatherTemperatureOffset(planetData):F1}℃，有效环境温度={GetGlobalAmbientTemperature():F1}℃");
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

    private float GetWeatherTemperatureOffset(PlanetData planetData)
    {
        if (planetData == null)
        {
            return 0f;
        }

        float intensity = Mathf.Clamp01(planetData.WeatherIntensity);
        return planetData.CurrentWeather switch
        {
            WeatherType.Cloudy => planetData.CloudyTemperatureOffset * intensity,
            WeatherType.Rain => planetData.RainTemperatureOffset * intensity,
            WeatherType.Storm => planetData.StormTemperatureOffset * intensity,
            _ => 0f
        };
    }

#endregion
}