using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>保存基础配置，组合任意外部来源，再一次发布最终业务规则。</summary>
public static partial class GameRuleConfiguration
{
    #region 配置来源与已发布快照

    private sealed class Modifier
    {
        public object Source;
        public Action<GameRuleValues> Apply;
        public int Priority;
        public long Order;
    }

    private static GameRuleValues baseValues = new();
    private static List<Modifier> modifiers = new();
    private static GameRuleSnapshot current = new(baseValues);
    private static long nextOrder;
    private static bool building;
    private static bool notifying;

    public static GameRuleSnapshot Current => current;
    public static long Version { get; private set; }
    public static event Action<GameRuleSnapshot> Changed;
    public static event Action<Exception> NotificationFailed;

    // 候选构建期间不允许修改注册表，避免来源回调递归污染事务。
    private static void RequireWritable()
    {
        if (building || notifying)
            throw new InvalidOperationException("业务规则不能在构建或发布通知回调中重入修改配置。");
    }

    public static void SetBaseValues(GameRuleValues values)
    {
        RequireWritable();
        if (values == null) throw new ArgumentNullException(nameof(values));
        GameRuleValues candidateBase = values.Clone();
        GameRuleSnapshot candidate = Build(candidateBase, modifiers);
        baseValues = candidateBase;
        Publish(candidate);
    }

    /// <summary>较小优先级先执行，同优先级按首次注册顺序；替换同来源不会累计修正。</summary>
    public static void SetModifier(object source, Action<GameRuleValues> modify, int priority = 0)
    {
        RequireWritable();
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (modify == null) throw new ArgumentNullException(nameof(modify));
        var candidateModifiers = new List<Modifier>(modifiers);
        int index = candidateModifiers.FindIndex(entry => Equals(entry.Source, source));
        var entry = new Modifier
        {
            Source = source, Apply = modify, Priority = priority,
            Order = index >= 0 ? candidateModifiers[index].Order : nextOrder
        };
        if (index >= 0) candidateModifiers[index] = entry;
        else candidateModifiers.Add(entry);
        candidateModifiers.Sort(CompareModifiers);
        GameRuleSnapshot candidate = Build(baseValues, candidateModifiers);
        modifiers = candidateModifiers;
        if (index < 0) nextOrder++;
        Publish(candidate);
    }

    public static bool RemoveModifier(object source)
    {
        RequireWritable();
        if (source == null) throw new ArgumentNullException(nameof(source));
        var candidateModifiers = new List<Modifier>(modifiers);
        int removed = candidateModifiers.RemoveAll(entry => Equals(entry.Source, source));
        if (removed == 0) return false;
        GameRuleSnapshot candidate = Build(baseValues, candidateModifiers);
        modifiers = candidateModifiers;
        Publish(candidate);
        return true;
    }

    /// <summary>切换世界时撤销全部会话来源，基础配置始终独立保留。</summary>
    public static void ResetSession()
    {
        RequireWritable();
        GameRuleSnapshot candidate = Build(baseValues, new List<Modifier>());
        modifiers = new List<Modifier>();
        nextOrder = 0;
        Publish(candidate);
    }

    /// <summary>换世界时一次撤销旧来源并装配新来源，不发布中间的空规则。</summary>
    public static void ResetSession(object source, Action<GameRuleValues> modify, int priority = 0)
    {
        RequireWritable();
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (modify == null) throw new ArgumentNullException(nameof(modify));
        var candidateModifiers = new List<Modifier>
        {
            new Modifier { Source = source, Apply = modify, Priority = priority, Order = 0 }
        };
        GameRuleSnapshot candidate = Build(baseValues, candidateModifiers);
        modifiers = candidateModifiers;
        nextOrder = 1;
        Publish(candidate);
    }

    private static int CompareModifiers(Modifier left, Modifier right)
    {
        int priority = left.Priority.CompareTo(right.Priority);
        return priority != 0 ? priority : left.Order.CompareTo(right.Order);
    }

    private static GameRuleSnapshot Build(GameRuleValues original, List<Modifier> sources)
    {
        building = true;
        try
        {
            original.Validate();
            GameRuleValues values = original.Clone();
            foreach (Modifier modifier in sources)
            {
                modifier.Apply(values);
                values.Validate();
            }
            return new GameRuleSnapshot(values);
        }
        finally { building = false; }
    }

    private static void Publish(GameRuleSnapshot candidate)
    {
        current = candidate;
        Version++;
        notifying = true;
        try
        {
            if (Changed == null) return;
            foreach (Action<GameRuleSnapshot> observer in Changed.GetInvocationList())
            {
                try { observer(candidate); }
                catch (Exception exception) { ReportNotificationFailure(exception); }
            }
        }
        finally { notifying = false; }
    }

    private static void ReportNotificationFailure(Exception exception)
    {
        if (NotificationFailed == null)
        {
            Debug.LogException(exception);
            return;
        }
        foreach (Action<Exception> observer in NotificationFailed.GetInvocationList())
        {
            try { observer(exception); }
            catch (Exception diagnosticException) { Debug.LogException(diagnosticException); }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        baseValues = new GameRuleValues();
        modifiers = new List<Modifier>();
        current = new GameRuleSnapshot(baseValues);
        nextOrder = 0;
        Version = 0;
        building = false;
        notifying = false;
        Changed = null;
        NotificationFailed = null;
    }

    #endregion
}
