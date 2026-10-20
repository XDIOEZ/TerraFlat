using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>食物规则总管：按优先级直接调用规则，不使用事件转发。</summary>
public sealed class FoodRulePipeline : IDisposable
{
    private readonly IFoodRuntimeContext context;
    private readonly List<IFoodMechanic> rules = new List<IFoodMechanic>();
    private readonly List<TickObserver> tickObservers = new(4);
    private readonly List<StateObserver> stateObservers = new(2);
    private bool initialized;

    #region 高频观察者索引

    private readonly struct TickObserver
    {
        public readonly IFoodMechanic Rule;
        public readonly IFoodTickObserver Callback;
        public readonly IFoodTickRequirement Requirement;

        public TickObserver(IFoodMechanic rule, IFoodTickObserver callback)
        {
            Rule = rule;
            Callback = callback;
            Requirement = rule as IFoodTickRequirement;
        }

        public bool ShouldTick => Requirement == null || Requirement.RequiresFoodTick;
    }

    private readonly struct StateObserver
    {
        public readonly IFoodMechanic Rule;
        public readonly IFoodStateObserver Callback;

        public StateObserver(IFoodMechanic rule, IFoodStateObserver callback)
        {
            Rule = rule;
            Callback = callback;
        }
    }

    internal int RuleCount => rules.Count;
    internal int TickObserverCount => tickObservers.Count;
    internal int StateObserverCount => stateObservers.Count;

    private void RegisterObservers(IFoodMechanic rule)
    {
        if (rule is IFoodTickObserver tick)
            tickObservers.Add(new TickObserver(rule, tick));
        if (rule is IFoodStateObserver state)
            stateObservers.Add(new StateObserver(rule, state));
    }

    private void RebuildObserverIndexes()
    {
        tickObservers.Clear();
        stateObservers.Clear();
        for (int i = 0; i < rules.Count; i++)
            RegisterObservers(rules[i]);
    }

    #endregion

    public FoodRulePipeline(IFoodRuntimeContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (context is FoodRuntimeContext runtimeContext)
            runtimeContext.RulePipeline = this;
    }

    public void Add(IFoodMechanic rule)
    {
        if (rule != null && !rules.Contains(rule))
        {
            rules.Add(rule);
            RegisterObservers(rule);
        }
    }

    public void Initialize()
    {
        if (initialized)
            return;

        rules.Sort((a, b) =>
        {
            int priority = a.Priority.CompareTo(b.Priority);
            return priority != 0
                ? priority
                : StringComparer.OrdinalIgnoreCase.Compare(a.MechanicId, b.MechanicId);
        });

        // 排序完成后只冻结观察者身份，Tick 资格仍实时读取。
        RebuildObserverIndexes();
        for (int i = 0; i < rules.Count; i++)
            RestoreState(rules[i]);
        initialized = true;
    }

    /// <summary>使用前检查。</summary>
    public bool CanUse(FoodUseContext useContext)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (!(rules[i] is IFoodUseGuard guard))
                continue;

            try
            {
                if (!guard.CanUse(useContext, out string reason))
                {
                    if (!string.IsNullOrWhiteSpace(reason))
                        Debug.LogWarning($"[FoodRule] 使用被阻止：{reason}");
                    return false;
                }
            }
            catch (Exception exception)
            {
                LogFailure(rules[i], "使用检查", exception);
            }
        }

        return true;
    }

    public void OnUse(FoodUseContext value) => Each("使用", rule =>
        (rule as IFoodUseRule)?.OnFoodUse(value));

    #region 规则通知

    /// <summary>判断当前规则集合是否存在需要持续调度的 Tick 观察者。</summary>
    public bool HasActiveTickObservers()
    {
        for (int i = 0; i < tickObservers.Count; i++)
        {
            if (tickObservers[i].ShouldTick)
                return true;
        }

        return false;
    }

    /// <summary>只推进当前声明为活跃的 Tick 观察者。</summary>
    public void OnTick(FoodTickContext value)
    {
        // 只遍历已登记的 Tick 观察者，保留每轮动态调度资格。
        for (int i = 0; i < tickObservers.Count; i++)
        {
            TickObserver observer = tickObservers[i];
            try
            {
                if (observer.ShouldTick)
                    observer.Callback.OnFoodTick(value);
            }
            catch (Exception exception)
            {
                LogFailure(observer.Rule, "Tick", exception);
            }
        }
    }

    public void OnConsumed(FoodConsumeResult value) => Each("吃完", rule =>
        (rule as IFoodConsumptionObserver)?.OnFoodConsumed(value));

    public void OnStateChanged(FoodStateChangedContext value)
    {
        for (int i = 0; i < stateObservers.Count; i++)
        {
            StateObserver observer = stateObservers[i];
            try
            {
                observer.Callback.OnFoodStateChanged(value);
            }
            catch (Exception exception)
            {
                LogFailure(observer.Rule, "状态刷新", exception);
            }
        }
    }

    public void OnRespawn() => Each("复活", rule =>
        (rule as IFoodRespawnRule)?.OnFoodRespawn());

    #endregion

    /// <summary>保存每条规则自己的负载。</summary>
    public void Save()
    {
        if (!initialized)
            return;

        context.PersistentData.MechanicStates ??= new List<FoodMechanicStateData>();
        for (int i = 0; i < rules.Count; i++)
        {
            if (!(rules[i] is IFoodMechanicStateProvider provider) ||
                string.IsNullOrWhiteSpace(provider.StateKey))
                continue;

            Run(rules[i], "保存", _ =>
            {
                FoodMechanicStateData state = context.PersistentData.MechanicStates.Find(
                    item => item != null && string.Equals(
                        item.StateKey, provider.StateKey, StringComparison.Ordinal));
                if (state == null)
                {
                    state = new FoodMechanicStateData { StateKey = provider.StateKey };
                    context.PersistentData.MechanicStates.Add(state);
                }

                state.Payload = provider.CaptureState();
            });
        }
    }

    public void Dispose()
    {
        if (!initialized)
            return;

        Save();
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            if (rules[i] is IDisposable disposable)
                Run(rules[i], "释放", _ => disposable.Dispose());
        }

        rules.Clear();
        tickObservers.Clear();
        stateObservers.Clear();
        initialized = false;
        if (context is FoodRuntimeContext runtimeContext &&
            ReferenceEquals(runtimeContext.RulePipeline, this))
            runtimeContext.RulePipeline = null;
    }

    private void Each(string stage, Action<IFoodMechanic> action)
    {
        for (int i = 0; i < rules.Count; i++)
            Run(rules[i], stage, action);
    }

    private void RestoreState(IFoodMechanic rule)
    {
        if (!(rule is IFoodMechanicStateProvider provider) ||
            string.IsNullOrWhiteSpace(provider.StateKey) ||
            context.PersistentData.MechanicStates == null)
            return;

        FoodMechanicStateData state = context.PersistentData.MechanicStates.Find(
            item => item != null && string.Equals(
                item.StateKey, provider.StateKey, StringComparison.Ordinal));
        if (state?.Payload != null)
            Run(rule, "恢复", _ => provider.RestoreState(state.Payload));
    }

    private static void Run(IFoodMechanic rule, string stage, Action<IFoodMechanic> action)
    {
        if (rule == null)
            return;

        try
        {
            action(rule);
        }
        catch (Exception exception)
        {
            LogFailure(rule, stage, exception);
        }
    }

    private static void LogFailure(IFoodMechanic rule, string stage, Exception exception)
    {
        Debug.LogError($"[FoodRule] {stage}失败，规则={rule?.MechanicId}：{exception.Message}");
    }
}
