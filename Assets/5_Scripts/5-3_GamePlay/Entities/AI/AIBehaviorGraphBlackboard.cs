using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 行为图的可存档运行态：保存当前状态时长、命名计时器和短时受击来源。
/// 普通计时器按模块 Tick 推进，草食维持计时器由世界时间事件单独推进。
/// </summary>
internal sealed class AIBehaviorGraphBlackboard
{
    #region 状态与计时器

    private readonly Dictionary<string, float> _timers = new(StringComparer.Ordinal); // 命名计时器剩余秒数。
    private readonly List<string> _timerKeys = new(); // 稳定迭代顺序。

    public float StateElapsed { get; set; } // 当前状态累计时长。
    public bool DamagedSinceStateEnter { get; private set; } // 本状态进入后是否受击。
    public bool ForageSatisfied { get; set; } // 本轮摄食是否完成。
    public bool ForageAvailable { get; set; } = true; // 本轮是否存在可用食物。
    public float RecentDamageRemaining { get; private set; } // 受击来源记忆剩余时长。
    public Vector3 RecentDamageOrigin { get; private set; } // 最近受击来源坐标。
    public bool HasRecentDamageThreat => RecentDamageRemaining > 0f;

    public AIBehaviorGraphBlackboard(
        IReadOnlyDictionary<string, float> savedTimers,
        float savedStateElapsed,
        float savedDamageRemaining,
        Vector3 savedDamageOrigin)
    {
        if (savedTimers != null)
        {
            foreach (KeyValuePair<string, float> pair in savedTimers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                _timerKeys.Add(pair.Key);
                _timers.Add(pair.Key, Mathf.Max(0f, pair.Value));
            }
        }

        StateElapsed = Mathf.Max(0f, savedStateElapsed);
        RecentDamageRemaining = Mathf.Max(0f, savedDamageRemaining);
        RecentDamageOrigin = savedDamageOrigin;
    }

    /// <summary>进入状态时清除仅作用于上一状态的瞬时事实。</summary>
    public void BeginState()
    {
        DamagedSinceStateEnter = false;
        StateElapsed = 0f;
    }

    /// <summary>推进普通计时器和受击记忆；世界计时器由权威世界时间推进。</summary>
    public void Tick(float deltaTime, string worldTimerKey)
    {
        float step = Mathf.Max(0f, deltaTime);
        for (int i = 0; i < _timerKeys.Count; i++)
        {
            string key = _timerKeys[i];
            if (string.Equals(key, worldTimerKey, StringComparison.Ordinal))
                continue;
            DecrementTimer(key, step);
        }

        RecentDamageRemaining = Mathf.Max(0f, RecentDamageRemaining - step);
    }

    /// <summary>推进一个按世界时间计量的计时器。</summary>
    public void TickWorldTimer(string key, float deltaTime)
    {
        DecrementTimer(key, Mathf.Max(0f, deltaTime));
    }

    /// <summary>记住一次有效伤害及可识别的来源。</summary>
    public void RecordDamage(float memoryDuration, bool hasOrigin, Vector3 origin)
    {
        DamagedSinceStateEnter = true;
        if (!hasOrigin || memoryDuration <= 0f)
            return;
        RecentDamageOrigin = origin;
        RecentDamageRemaining = memoryDuration;
    }

    public bool TryGetRecentDamageOrigin(out Vector3 origin)
    {
        origin = RecentDamageOrigin;
        return HasRecentDamageThreat;
    }

    public void SetTimer(string key, float seconds)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("AI 行为图计时器键不能为空。", nameof(key));
        if (!_timers.ContainsKey(key))
            _timerKeys.Add(key);
        _timers[key] = Mathf.Max(0f, seconds);
    }

    public float GetTimerRemaining(string key)
    {
        return _timers.TryGetValue(key, out float remain) ? remain : 0f;
    }

    public bool IsTimerElapsed(string key)
    {
        return GetTimerRemaining(key) <= 0f;
    }

    public bool HasTimer(string key)
    {
        return _timers.ContainsKey(key);
    }

    public Dictionary<string, float> ExportTimers()
    {
        return new Dictionary<string, float>(_timers, StringComparer.Ordinal);
    }

    private void DecrementTimer(string key, float step)
    {
        if (_timers.TryGetValue(key, out float remain) && remain > 0f)
            _timers[key] = Mathf.Max(0f, remain - step);
    }

    #endregion
}
