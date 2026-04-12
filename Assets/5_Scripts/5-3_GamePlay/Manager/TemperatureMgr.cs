using System;
using UnityEngine;

public class TemperatureMgr : SingletonAutoMono<TemperatureMgr>
{
#region 字段

    public bool EnableDebugLog = false; // 是否输出温度处理调试日志

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

        data.ComfortableMax = Mathf.Max(data.ComfortableMin, data.ComfortableMax);
        data.HotDamageStart = Mathf.Max(data.ColdDamageStart, data.HotDamageStart);
        data.CriticalHigh = Mathf.Max(data.HotDamageStart, data.CriticalHigh);
        data.CriticalLow = Mathf.Min(data.ColdDamageStart, data.CriticalLow);
    }

    public void ProcessTemperature(
        Mod_Temperature.TemperatureData data,
        DamageReceiver damageReceiver,
        float deltaTime,
        Action<float> onTemperatureChanged)
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

        float damage = EvaluateTemperatureDamage(data, deltaTime);
        if (damage <= 0f)
        {
            return;
        }

        damageReceiver.ForceHurt(damage);

        if (EnableDebugLog)
        {
            Debug.Log($"[TemperatureMgr] 触发温度伤害，当前体温={data.CurrentTemperature:F2}℃，伤害={damage:F3}");
        }
    }

    public float EvaluateNextTemperature(Mod_Temperature.TemperatureData data, float deltaTime)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        float targetTemperature = data.AmbientTemperature + data.Insulation;
        return Mathf.MoveTowards(data.CurrentTemperature, targetTemperature, data.ChangeSpeed * deltaTime);
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