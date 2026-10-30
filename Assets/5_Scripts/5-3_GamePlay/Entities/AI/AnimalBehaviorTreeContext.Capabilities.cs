using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region 生物能力组合与持久运行数据

/// <summary>能力维护自己的事实和计时，动作选择只由行为树负责。</summary>
public abstract class AnimalBehaviorTreeCapability : IDisposable
{
    public virtual void Tick(float deltaTime) { }
    public virtual JObject CaptureSaveData() => new();
    public virtual void Dispose() { }
}

public sealed partial class AnimalBehaviorTreeContext
{
    private readonly Dictionary<string, AnimalBehaviorTreeCapability> _capabilities = new(StringComparer.Ordinal);
    private readonly List<AnimalBehaviorTreeCapability> _capabilityList = new();
    private JObject _capabilityConfiguration = new();
    private JObject _savedCapabilities = new();
    private string _activeAnimation;
    public string CurrentAction { get; private set; }
    public AIStateAnimationRole AnimationRole { get; private set; }

    public void ConfigureCapabilities(JObject configuration, JObject saved)
    {
        _capabilityConfiguration = configuration ?? new JObject();
        _savedCapabilities = saved ?? new JObject();
    }

    public JObject GetCapabilityConfiguration(string key)
        => _capabilityConfiguration[key] as JObject ?? new JObject();

    public T GetOrCreateCapability<T>(string key, Func<JObject, T> factory) where T : AnimalBehaviorTreeCapability
    {
        if (_capabilities.TryGetValue(key, out AnimalBehaviorTreeCapability existing)) return (T)existing;
        T capability = factory(_savedCapabilities[key] as JObject);
        _capabilities.Add(key, capability);
        _capabilityList.Add(capability);
        return capability;
    }

    public bool TryGetCapability<T>(string key, out T capability) where T : AnimalBehaviorTreeCapability
    {
        _capabilities.TryGetValue(key, out AnimalBehaviorTreeCapability value);
        capability = value as T;
        return capability != null;
    }

    public JObject CaptureCapabilityData()
    {
        var result = new JObject();
        foreach (KeyValuePair<string, AnimalBehaviorTreeCapability> pair in _capabilities)
            result[pair.Key] = pair.Value.CaptureSaveData();
        return result;
    }

    /// <summary>生物死亡时关闭所有组合攻击能力，不依赖配置键名。</summary>
    public void StopAllCombatCapabilities()
    {
        foreach (AnimalBehaviorTreeCapability capability in _capabilityList)
            if (capability is AnimalCombatCapability combat)
            {
                combat.CancelSkills();
                combat.StopAttack(true);
            }
    }

    public void BeginAction(string key, string animation = null, AIStateAnimationRole role = AIStateAnimationRole.Action)
    {
        if (!string.Equals(CurrentAction, key, StringComparison.Ordinal))
        {
            CurrentAction = key;
            BeginState();
        }
        AnimationRole = role;
        if (!string.IsNullOrWhiteSpace(animation) && !string.Equals(_activeAnimation, animation, StringComparison.Ordinal) && Animator != null)
        {
            bool played = Animator.PlayAnimation(animation);
            string fallback = role == AIStateAnimationRole.Moving ? "Move" : "Stand";
            if (!played && animation != fallback) played = Animator.PlayAnimation(fallback);
            if (played) _activeAnimation = animation;
        }
    }

    public bool IsActionActive(string key)
        => string.Equals(CurrentAction, key, StringComparison.Ordinal) ||
           CurrentAction != null && CurrentAction.EndsWith("." + key, StringComparison.Ordinal);

    public void EndAction() => CurrentAction = null;
}

#endregion
