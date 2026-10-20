using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

/// <summary>各天气区独立平滑三档推动向量，同一帧所有实体共用结果。</summary>
public sealed class RegionalWindPushField
{
    #region 区域推动响应

    private readonly struct PushResponse
    {
        private readonly Vector2 level8;
        private readonly Vector2 level9;
        private readonly Vector2 level10;

        private PushResponse(Vector2 level8, Vector2 level9, Vector2 level10)
        {
            this.level8 = level8;
            this.level9 = level9;
            this.level10 = level10;
        }

        public bool HasPush => level8.sqrMagnitude > 0f || level9.sqrMagnitude > 0f || level10.sqrMagnitude > 0f;

        public Vector2 ForLevel(int requiredLevel) => requiredLevel switch
        {
            8 => level8,
            9 => level9,
            10 => level10,
            _ => Vector2.zero
        };

        public bool Equals(PushResponse other)
            => level8.Equals(other.level8) && level9.Equals(other.level9) && level10.Equals(other.level10);

        public static PushResponse FromWind(float3 wind) => new(
            WindEnvironmentRules.ResolveSourceVelocity(wind, 8),
            WindEnvironmentRules.ResolveSourceVelocity(wind, 9),
            WindEnvironmentRules.ResolveSourceVelocity(wind, 10));

        public static PushResponse Lerp(PushResponse from, PushResponse to, float amount) => new(
            Vector2.Lerp(from.level8, to.level8, amount),
            Vector2.Lerp(from.level9, to.level9, amount),
            Vector2.Lerp(from.level10, to.level10, amount));
    }

    private sealed class SourceState
    {
        public PushResponse Current;
        public PushResponse Target;
        public ulong SeenGeneration;
        private PushResponse start;
        private double startedTime;
        private float transitionSeconds;
        private bool transitioning;

        public void SetTarget(PushResponse target, double now, float seconds)
        {
            Advance(now);
            if (Target.Equals(target)) return;
            // 改档、停风和反向都从此刻真实推速续接，不重新从旧档或零开始。
            start = Current;
            Target = target;
            startedTime = now;
            transitionSeconds = seconds;
            transitioning = seconds > 0f;
            if (!transitioning) Current = target;
        }

        public void Advance(double now)
        {
            if (!transitioning) return;
            float progress = transitionSeconds > 0f ? (float)((now - startedTime) / transitionSeconds) : 1f;
            if (progress >= 1f)
            {
                Current = Target;
                transitioning = false;
                return;
            }
            Current = PushResponse.Lerp(start, Target, Mathf.SmoothStep(0f, 1f, progress));
        }
    }

    #endregion

    #region 整批同步与生命周期

    private readonly Dictionary<Int2, SourceState> sources = new();
    private readonly Action<Int2, float3> synchronizeSource;
    private RegionalWeatherEngine engine;
    private ulong sourceRevision;
    private ulong generation;
    private double currentTime;
    private float currentTransitionSeconds;
    public bool HasWindPush { get; private set; }

    public RegionalWindPushField() => synchronizeSource = SynchronizeSource;

    /// <summary>使用独立单调秒轴推进响应，天气原始强度、存档和游戏时钟保持各自语义。</summary>
    public void Synchronize(RegionalWeatherEngine sourceEngine, double now, float transitionSeconds)
    {
        if (sourceEngine == null)
        {
            Reset();
            return;
        }
        if (!ReferenceEquals(engine, sourceEngine))
        {
            Reset();
            engine = sourceEngine;
        }
        currentTime = double.IsFinite(now) ? Math.Max(currentTime, now) : currentTime;
        currentTransitionSeconds = float.IsFinite(transitionSeconds) ? Mathf.Max(0f, transitionSeconds) : 3f;
        if (sourceRevision != engine.WindQueryRevision)
        {
            generation++;
            engine.VisitWindSources(synchronizeSource);
            foreach (SourceState source in sources.Values)
                if (source.SeenGeneration != generation)
                    source.SetTarget(default, currentTime, currentTransitionSeconds);
            sourceRevision = engine.WindQueryRevision;
        }
        HasWindPush = false;
        foreach (SourceState source in sources.Values)
        {
            source.Advance(currentTime);
            // 起风首帧和停风尾段均继续调度，不能只看原始风强是否仍高于八级。
            HasWindPush |= source.Current.HasPush || source.Target.HasPush;
        }
    }

    private void SynchronizeSource(Int2 region, float3 wind)
    {
        if (!sources.TryGetValue(region, out SourceState source))
        {
            source = new SourceState();
            sources.Add(region, source);
        }
        source.SeenGeneration = generation;
        source.SetTarget(PushResponse.FromWind(wind), currentTime, currentTransitionSeconds);
    }

    public void Reset()
    {
        sources.Clear();
        engine = null;
        sourceRevision = ulong.MaxValue;
        generation = 0;
        currentTime = 0d;
        HasWindPush = false;
    }

    #endregion

    #region 连续坐标采样

    /// <summary>先读取各区已经过渡的推动速度，再沿用天气区同一空间权重混合。</summary>
    public Vector2 Sample(double2 position, int requiredWindLevel)
    {
        if (!HasWindPush || requiredWindLevel < WindEnvironmentRules.MinimumPushWindLevel ||
            requiredWindLevel > WindEnvironmentRules.MaximumWindLevel || engine == null ||
            !engine.TryGetWindBlend(position, out RegionalWindBlend blend))
            return Vector2.zero;
        return WindEnvironmentRules.BlendPushVelocities(in blend,
            Read(blend.OriginRegion, requiredWindLevel), Read(blend.HorizontalRegion, requiredWindLevel),
            Read(blend.VerticalRegion, requiredWindLevel), Read(blend.DiagonalRegion, requiredWindLevel));
    }

    private Vector2 Read(Int2 region, int requiredWindLevel)
        => sources.TryGetValue(region, out SourceState source)
            ? source.Current.ForLevel(requiredWindLevel) : Vector2.zero;

    #endregion
}
