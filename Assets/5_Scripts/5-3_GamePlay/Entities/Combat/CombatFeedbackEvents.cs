using System;
using UnityEngine;

/// <summary>发布战斗结果的纯表现通知，不参与命中概率、伤害或投射物结算。</summary>
public static class CombatFeedbackEvents
{
    #region 未命中反馈

    public static event Action<Vector2> LogicalMissed;
    public static event Action<Vector2> EquipmentBroken;

    public static void PublishEquipmentBroken(Vector2 position) => EquipmentBroken?.Invoke(position);

    /// <summary>只传递本次攻击位置快照，避免表现层重新寻找目标或重算命中。</summary>
    public static void PublishLogicalMiss(Vector2 attackPosition)
    {
        LogicalMissed?.Invoke(attackPosition);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        LogicalMissed = null;
        EquipmentBroken = null;
    }

    #endregion
}
