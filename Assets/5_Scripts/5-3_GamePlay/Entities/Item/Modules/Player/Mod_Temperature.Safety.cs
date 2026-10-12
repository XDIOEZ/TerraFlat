using System;
using System.Collections.Generic;
using UnityEngine;

public static class TemperatureConditionBuffIds
{
    public const string Hypothermia = "低温冻伤";
    public const string Heatstroke = "热射病";
}

/// <summary>角色安全体表温度范围、温度疾病与 AI 危险位置避让。</summary>
public partial class Mod_Temperature
{
    #region 安全体温范围

    private const string HypothermiaSource = "temperature:hypothermia";
    private const string HeatstrokeSource = "temperature:heatstroke";

    private readonly Dictionary<object, Vector2> safeTemperatureRangeModifiers = new();
    private Mod_BuffManager temperatureBuffManager;
    private ITemperatureSafetyMovement temperatureSafetyMovement;

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

    #region AI 危险记忆与方向引导

    private const int DangerPositionCapacity = 8;
    private const float DangerPositionRecordDistance = 1f;
    private const float DangerMemorySeconds = 30f;
    private const float DangerInfluenceRadius = 8f;
    private const float SafetyClearanceDistance = 3f;
    private const float SafetySettleSeconds = 2f;
    private const float EscapeSegmentDistance = 3f;
    private const float EscapeRetargetSeconds = 2f;
    private const float EscapeMinimumRetargetSeconds = 0.5f;

    private struct TemperatureDangerMemory
    {
        public Vector2 Position;
        public float RemainingSeconds;
    }

    private readonly List<TemperatureDangerMemory> recentDangerPositions = new(DangerPositionCapacity);
    private bool temperatureSafetyRetreatActive;
    private bool hasTemperatureSafetyLastPosition;
    private Vector2 temperatureSafetyLastPosition;
    private Vector2 temperatureEscapeDirection;
    private Vector2 temperatureEscapeSegmentOrigin;
    private float temperatureEscapeSegmentElapsed;
    private float temperatureSafeElapsed;

    public bool HasTemperatureDangerMemory => recentDangerPositions.Count > 0;

    /// <summary>普通移动也保留危险点的方向偏置，避免避险结束后立即折返。</summary>
    public Vector2 BiasTemperatureMovementDestination(Vector2 origin, Vector2 destination)
    {
        Vector2 offset = WorldTopologyRuntime.ShortestDelta(origin, destination);
        Vector2 bias = GetTemperatureAvoidanceDirection(origin, offset);
        if (offset.sqrMagnitude <= 0.0001f || bias.sqrMagnitude <= 0.0001f)
            return destination;

        Vector2 direction = offset.normalized + bias * 2f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = temperatureEscapeDirection;
        if ((direction.normalized - offset.normalized).sqrMagnitude <= 0.0001f)
            return destination;

        direction = RotateTemperatureEscapeDirection(direction.normalized, UnityEngine.Random.Range(-25f, 25f));
        return WorldTopologyRuntime.NormalizePosition(
            origin + direction * Mathf.Min(offset.magnitude, EscapeSegmentDistance));
    }

    private Vector2 GetTemperatureAvoidanceDirection(Vector2 origin, Vector2 requestedOffset = default)
    {
        Vector2 bias = Vector2.zero;
        Vector2 strongestBias = Vector2.zero;
        Vector2 pathBias = Vector2.zero;
        Vector2 strongestPathBias = Vector2.zero;
        foreach (TemperatureDangerMemory memory in recentDangerPositions)
        {
            Vector2 away = WorldTopologyRuntime.ShortestDelta(memory.Position, origin);
            float distance = away.magnitude;
            float approach = Vector2.Dot(requestedOffset, -away);
            float pathFraction = requestedOffset.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(approach / requestedOffset.sqrMagnitude)
                : 0f;
            bool headsIntoDanger = approach > 0f &&
                (-away - requestedOffset * pathFraction).sqrMagnitude <
                SafetyClearanceDistance * SafetyClearanceDistance;
            if (!headsIntoDanger && distance >= DangerInfluenceRadius)
                continue;

            // 目标线路会穿过已知危险时保留强偏置，不能一走远就又被原目标拉回。
            float weight = (headsIntoDanger ? 1f : 1f - distance / DangerInfluenceRadius) *
                           Mathf.Clamp01(memory.RemainingSeconds / SafetySettleSeconds);
            Vector2 contribution = (distance > 0.001f ? away / distance : temperatureEscapeDirection) * weight;
            bias += contribution;
            if (contribution.sqrMagnitude > strongestBias.sqrMagnitude)
                strongestBias = contribution;
            if (headsIntoDanger)
            {
                pathBias += contribution;
                if (contribution.sqrMagnitude > strongestPathBias.sqrMagnitude)
                    strongestPathBias = contribution;
            }
        }

        // 前进线路上的危险优先，不能被身后危险点的普通偏向抵消。
        if (strongestPathBias.sqrMagnitude > 0.0001f)
            return Vector2.ClampMagnitude(pathBias.sqrMagnitude > 0.0001f ? pathBias : strongestPathBias, 1f);
        // 两侧危险向量相抵时保留最强方向，不能因零向量停止避险。
        return Vector2.ClampMagnitude(bias.sqrMagnitude > 0.0001f ? bias : strongestBias, 1f);
    }

    private void InitializeTemperatureSafety()
    {
        temperatureBuffManager = item?.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        temperatureSafetyMovement = ResolveTemperatureSafetyMovement();
        temperatureSafetyRetreatActive = false;
        ResetTemperatureDangerMemory();
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

        float seconds = Mathf.Max(0f, deltaTime);
        AgeTemperatureDangerMemory(seconds);
        Vector2 current = item.transform.position;
        Vector2 movement = hasTemperatureSafetyLastPosition
            ? WorldTopologyRuntime.ShortestDelta(temperatureSafetyLastPosition, current)
            : Vector2.zero;
        temperatureSafetyLastPosition = current;
        hasTemperatureSafetyLastPosition = true;

        float effectiveAmbient = Data.AmbientTemperature + Data.RuntimeAmbientOffset;
        bool bodySafe = IsTemperatureSafe(Data.CurrentTemperature);
        bool ambientSafe = IsTemperatureSafe(effectiveAmbient);

        if (!ambientSafe)
        {
            if (!temperatureSafetyRetreatActive)
            {
                temperatureEscapeDirection = movement.sqrMagnitude > 0.0001f
                    ? -movement.normalized
                    : RandomTemperatureEscapeDirection();
            }
            RecordTemperatureDangerPosition(current);
        }

        temperatureSafeElapsed = ambientSafe ? temperatureSafeElapsed + seconds : 0f;
        bool hasClearance = HasTemperatureDangerClearance(current);
        if (ambientSafe && bodySafe &&
            (!temperatureSafetyRetreatActive || (hasClearance && temperatureSafeElapsed >= SafetySettleSeconds)))
        {
            ClearTemperatureSafetyRetreat();
            return;
        }

        bool started = !temperatureSafetyRetreatActive;
        temperatureEscapeSegmentElapsed += seconds;
        if (!started && temperatureEscapeSegmentElapsed < EscapeMinimumRetargetSeconds)
            return;
        if (!started && !temperatureSafetyMovement.ShouldAdvanceTemperatureSafetyDestination &&
            temperatureEscapeSegmentElapsed < EscapeRetargetSeconds)
            return;

        // 已离开危险边界但体温仍需恢复时才等待，不在刚迈出危险区时立刻折返。
        if (ambientSafe && hasClearance && !bodySafe &&
            (!HasTemperatureDangerMemory || temperatureSafeElapsed >= SafetySettleSeconds))
            temperatureSafetyMovement.SetTemperatureSafetyDestination(current);
        else
            ChooseTemperatureEscapeDestination(current, started);
        temperatureSafetyRetreatActive = true;
        temperatureEscapeSegmentOrigin = current;
        temperatureEscapeSegmentElapsed = 0f;
    }

    private void AgeTemperatureDangerMemory(float seconds)
    {
        for (int index = recentDangerPositions.Count - 1; index >= 0; index--)
        {
            TemperatureDangerMemory memory = recentDangerPositions[index];
            memory.RemainingSeconds -= seconds;
            if (memory.RemainingSeconds <= 0f)
                recentDangerPositions.RemoveAt(index);
            else
                recentDangerPositions[index] = memory;
        }
    }

    private void RecordTemperatureDangerPosition(Vector2 current)
    {
        for (int index = 0; index < recentDangerPositions.Count; index++)
        {
            TemperatureDangerMemory memory = recentDangerPositions[index];
            if (WorldTopologyRuntime.SqrDistance(memory.Position, current) >
                DangerPositionRecordDistance * DangerPositionRecordDistance)
                continue;

            // 只续期而不移动锚点，保证危险点指向角色的向量不会一直归零。
            memory.RemainingSeconds = DangerMemorySeconds;
            recentDangerPositions[index] = memory;
            return;
        }

        if (recentDangerPositions.Count >= DangerPositionCapacity)
            recentDangerPositions.RemoveAt(0);
        recentDangerPositions.Add(new TemperatureDangerMemory
        {
            Position = WorldTopologyRuntime.NormalizePosition(current),
            RemainingSeconds = DangerMemorySeconds
        });
    }

    private bool HasTemperatureDangerClearance(Vector2 current)
    {
        foreach (TemperatureDangerMemory memory in recentDangerPositions)
            if (WorldTopologyRuntime.SqrDistance(memory.Position, current) <
                SafetyClearanceDistance * SafetyClearanceDistance)
                return false;
        return true;
    }

    private void ChooseTemperatureEscapeDestination(Vector2 current, bool started)
    {
        bool stalled = !started &&
            WorldTopologyRuntime.SqrDistance(temperatureEscapeSegmentOrigin, current) < 0.01f;
        Vector2 bias = GetTemperatureAvoidanceDirection(current);
        Vector2 heading = bias.sqrMagnitude > 0.0001f ? bias.normalized : temperatureEscapeDirection;
        if (heading.sqrMagnitude <= 0.0001f)
            heading = RandomTemperatureEscapeDirection();

        temperatureEscapeDirection = RotateTemperatureEscapeDirection(heading,
            UnityEngine.Random.Range(stalled ? -110f : -35f, stalled ? 110f : 35f));
        temperatureSafetyMovement.SetTemperatureSafetyDestination(WorldTopologyRuntime.NormalizePosition(
            current + temperatureEscapeDirection * EscapeSegmentDistance));
    }

    private static Vector2 RandomTemperatureEscapeDirection()
    {
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static Vector2 RotateTemperatureEscapeDirection(Vector2 direction, float angleDegrees)
    {
        float angle = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(
            direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
            direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle));
    }

    private void ResetTemperatureDangerMemory()
    {
        recentDangerPositions.Clear();
        hasTemperatureSafetyLastPosition = false;
        temperatureEscapeDirection = Vector2.zero;
        temperatureEscapeSegmentElapsed = 0f;
        temperatureSafeElapsed = 0f;
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
        ResetTemperatureDangerMemory();
        safeTemperatureRangeModifiers.Clear();
        temperatureSafetyMovement = null;
        temperatureBuffManager = null;
    }

    #endregion
}
