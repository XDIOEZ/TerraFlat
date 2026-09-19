using UnityEngine;

/// <summary>使用命中时冻结的来源坐标；投射物被回收或 ECS 来源没有 Item 时仍能确定逃离方向。</summary>
public static class DamageThreatOrigin
{
    #region 受伤威胁坐标

    public static bool TryResolve(DamageReceiverDamageInfo info, out Vector2 position)
    {
        position = default;
        if (info == null) return false;
        if (info.Context.Attack.Source.IsValid && info.Context.Attack.Source.Backend != FlatWorld.Combat.CombatBackend.Environment)
        {
            position = info.Context.Origin;
            return true;
        }
        if (info.Attacker == null) return false;
        position = info.Attacker.transform.position;
        return true;
    }

    #endregion
}
