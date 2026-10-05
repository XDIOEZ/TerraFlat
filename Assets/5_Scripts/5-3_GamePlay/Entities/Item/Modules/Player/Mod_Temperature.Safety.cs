using System;
using System.Collections.Generic;
using UnityEngine;

public static class TemperatureConditionBuffIds
{
    public const string Hypothermia = "低温冻伤";
    public const string Heatstroke = "热射病";
}

/// <summary>角色安全体表温度范围、温度疾病与 AI 安全位置回退。</summary>
public partial class Mod_Temperature
{
    #region 安全体温范围

    private const int SafePositionCapacity = 4;
    private const float SafePositionRecordDistance = 0.75f;
    private const string HypothermiaSource = "temperature:hypothermia";
    private const string HeatstrokeSource = "temperature:heatstroke";

    private readonly Dictionary<object, Vector2> safeTemperatureRangeModifiers = new();
    private readonly List<Vector2> recentSafePositions = new(SafePositionCapacity);
    private Mod_BuffManager temperatureBuffManager;
    private ITemperatureSafetyMovement temperatureSafetyMovement;
    private bool temperatureSafetyRetreatActive;

    public float SafeTemperatureMin
    {
        get
        {
            GetSafeTemperatureRange(out float minimum, out _);
            return minimum;
        }
    }

    public float SafeTemperatureMax
    {
        get
        {
            GetSafeTemperatureRange(out _, out float maximum);
            return maximum;
        }
    }

    /// <summary>装备等来源可独立扩展安全范围；负下限偏移提升耐寒，正上限偏移提升耐热。</summary>
    public void SetSafeTemperatureRangeModifier(object source, float minimumDelta, float maximumDelta)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (!float.IsFinite(minimumDelta) || !float.IsFinite(maximumDelta))
            throw new ArgumentOutOfRangeException(nameof(minimumDelta), "安全体温范围修正必须是有限数值。");

        safeTemperatureRangeModifiers[source] = new Vector2(minimumDelta, maximumDelta);
    }

    public void RemoveSafeTemperatureRangeModifier(object source)
    {
        if (source != null)
            safeTemperatureRangeModifiers.Remove(source);
    }

    public void GetSafeTemperatureRange(out float minimum, out float maximum)
    {
        minimum = Data.SafeTemperatureMin;
        maximum = Data.SafeTemperatureMax;
        foreach (Vector2 modifier in safeTemperatureRangeModifiers.Values)
        {
            minimum += modifier.x;
            maximum += modifier.y;
        }

        maximum = Mathf.Max(minimum, maximum);
    }

    public bool IsTemperatureSafe(float temperature)
    {
        GetSafeTemperatureRange(out float minimum, out float maximum);
        return temperature >= minimum && temperature <= maximum;
    }

    #endregion

    #region 温度疾病

    private void UpdateTemperatureConditionBuffs(float deltaTime)
    {
        GetSafeTemperatureRange(out float minimum, out float maximum);
        bool tooCold = Data.CurrentTemperature < minimum;
        bool tooHot = Data.CurrentTemperature > maximum;
        float seconds = Mathf.Max(0f, deltaTime);
        // 安全时逐秒补回缓冲，冷热切换与短暂脱离危险均不能直接重置。
        Data.RemainingTemperatureBufferSeconds = Mathf.Clamp(
            Data.RemainingTemperatureBufferSeconds +
            (tooCold || tooHot ? -seconds : seconds * Data.TemperatureBufferRecoveryPerSecond),
            0f,
            Data.DangerTemperatureBufferSeconds);

        if (temperatureBuffManager == null)
            return;

        bool bufferExhausted = Data.RemainingTemperatureBufferSeconds <= 0f;
        if (tooCold && bufferExhausted)
            temperatureBuffManager.EnsureSourceBuff(TemperatureConditionBuffIds.Hypothermia, HypothermiaSource);
        else
            temperatureBuffManager.RemoveSourceBuff(HypothermiaSource);

        if (tooHot && bufferExhausted)
            temperatureBuffManager.EnsureSourceBuff(TemperatureConditionBuffIds.Heatstroke, HeatstrokeSource);
        else
            temperatureBuffManager.RemoveSourceBuff(HeatstrokeSource);
    }

    private void ClearTemperatureConditionBuffs()
    {
        temperatureBuffManager?.RemoveSourceBuff(HypothermiaSource);
        temperatureBuffManager?.RemoveSourceBuff(HeatstrokeSource);
    }

    #endregion

    #region AI 安全位置回退

    private void InitializeTemperatureSafety()
    {
        temperatureBuffManager = item?.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        temperatureSafetyMovement = ResolveTemperatureSafetyMovement();
        temperatureSafetyRetreatActive = false;
        recentSafePositions.Clear();
    }

    private ITemperatureSafetyMovement ResolveTemperatureSafetyMovement()
    {
        if (item?.itemMods?.Mods == null)
            return null;

        ITemperatureSafetyMovement best = null;
        int bestPriority = int.MinValue;
        foreach (Module module in item.itemMods.Mods.Values)
        {
            if (module is not ITemperatureSafetyMovement candidate ||
                candidate.TemperatureSafetyMovementPriority <= bestPriority)
            {
                continue;
            }

            best = candidate;
            bestPriority = candidate.TemperatureSafetyMovementPriority;
        }

        return best;
    }

    private void UpdateTemperatureSafety(float deltaTime)
    {
        UpdateTemperatureConditionBuffs(deltaTime);
        if (temperatureSafetyMovement == null)
            return;

        float effectiveAmbient = Data.AmbientTemperature + Data.RuntimeAmbientOffset;
        bool bodySafe = IsTemperatureSafe(Data.CurrentTemperature);
        bool ambientSafe = IsTemperatureSafe(effectiveAmbient);

        if (bodySafe && ambientSafe)
        {
            ClearTemperatureSafetyRetreat();
            RecordCurrentSafePosition();
            return;
        }

        if (!ambientSafe)
        {
            if (!temperatureSafetyRetreatActive ||
                temperatureSafetyMovement.ShouldAdvanceTemperatureSafetyDestination)
            {
                TryRetreatToPreviousSafePosition();
            }
            return;
        }

        // 已回到安全环境但体温尚未恢复时原地等待，避免普通 AI 再次走入危险区域。
        if (!temperatureSafetyRetreatActive)
        {
            temperatureSafetyRetreatActive = true;
            temperatureSafetyMovement.SetTemperatureSafetyDestination(item.transform.position);
        }
    }

    private void RecordCurrentSafePosition()
    {
        if (temperatureSafetyMovement == null || item == null)
            return;

        float effectiveAmbient = Data.AmbientTemperature + Data.RuntimeAmbientOffset;
        if (!IsTemperatureSafe(Data.CurrentTemperature) || !IsTemperatureSafe(effectiveAmbient))
            return;

        Vector2 current = item.transform.position;
        if (recentSafePositions.Count > 0 &&
            WorldTopologyRuntime.SqrDistance(recentSafePositions[^1], current) <
            SafePositionRecordDistance * SafePositionRecordDistance)
        {
            return;
        }

        recentSafePositions.Add(WorldTopologyRuntime.NormalizePosition(current));
        if (recentSafePositions.Count > SafePositionCapacity)
            recentSafePositions.RemoveAt(0);
    }

    private void TryRetreatToPreviousSafePosition()
    {
        if (recentSafePositions.Count == 0)
            return;

        Vector2 current = item.transform.position;
        int index = 0;
        float nearestDistance = float.PositiveInfinity;
        for (int candidateIndex = 0; candidateIndex < recentSafePositions.Count; candidateIndex++)
        {
            float distance = WorldTopologyRuntime.SqrDistance(current, recentSafePositions[candidateIndex]);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            index = candidateIndex;
        }

        Vector2 destination = recentSafePositions[index];
        recentSafePositions.RemoveAt(index);
        temperatureSafetyRetreatActive = true;
        temperatureSafetyMovement.SetTemperatureSafetyDestination(destination);
    }

    private void ClearTemperatureSafetyRetreat()
    {
        if (!temperatureSafetyRetreatActive)
            return;

        temperatureSafetyRetreatActive = false;
        temperatureSafetyMovement?.ClearTemperatureSafetyDestination();
    }

    private void ResetTemperatureSafety()
    {
        ClearTemperatureConditionBuffs();
        ClearTemperatureSafetyRetreat();
        recentSafePositions.Clear();
        safeTemperatureRangeModifiers.Clear();
        temperatureSafetyMovement = null;
        temperatureBuffManager = null;
    }

    #endregion
}
