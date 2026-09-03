using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>食物规则总管：按优先级直接调用规则，不使用事件转发。</summary>
public sealed class FoodRulePipeline : IDisposable
{
    private readonly IFoodRuntimeContext context;
    private readonly List<IFoodMechanic> rules = new List<IFoodMechanic>();
    private bool initialized;

    public FoodRulePipeline(IFoodRuntimeContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (context is FoodRuntimeContext runtimeContext)
            runtimeContext.RulePipeline = this;
    }

    public void Add(IFoodMechanic rule)
    {
        if (rule != null && !rules.Contains(rule))
            rules.Add(rule);
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

    /// <summary>判断当前规则集合是否存在需要持续调度的 Tick 观察者。</summary>
    public bool HasActiveTickObservers()
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (ShouldTick(rules[i]))
                return true;
        }

        return false;
    }

    /// <summary>只推进当前声明为活跃的 Tick 观察者。</summary>
    public void OnTick(FoodTickContext value) => Each("Tick", rule =>
    {
        if (ShouldTick(rule))
            ((IFoodTickObserver)rule).OnFoodTick(value);
    });

    public void OnConsumed(FoodConsumeResult value) => Each("吃完", rule =>
        (rule as IFoodConsumptionObserver)?.OnFoodConsumed(value));

    public void OnStateChanged(FoodStateChangedContext value) => Each("状态刷新", rule =>
        (rule as IFoodStateObserver)?.OnFoodStateChanged(value));

    public void OnRespawn() => Each("复活", rule =>
        (rule as IFoodRespawnRule)?.OnFoodRespawn());

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

    /// <summary>兼容未声明调度策略的扩展规则，并过滤明确休眠的观察者。</summary>
    private static bool ShouldTick(IFoodMechanic rule)
    {
        if (!(rule is IFoodTickObserver))
            return false;

        return !(rule is IFoodTickRequirement requirement) || requirement.RequiresFoodTick;
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
