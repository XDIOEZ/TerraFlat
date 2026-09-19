using System;
using FlatWorld.Combat;
using UnityEngine;

/// <summary>不依赖场景、存档或 Play 的确定性鸟规则诊断；可由菜单或 Unity -executeMethod 调用。</summary>
public static class BirdActorRuleDiagnostics
{
    #region 纯规则校验
    public static void Validate()
    {
        foreach (BirdFlightPhase phase in Enum.GetValues(typeof(BirdFlightPhase)))
        {
            bool grounded = phase == BirdFlightPhase.Ground;
            Require(AI_Bird.AllowsDamage(phase, CombatDeliveryCapabilities.None) == grounded, "近战阶段筛选错误");
            Require(AI_Bird.AllowsDamage(phase, CombatDeliveryCapabilities.Projectile) == grounded, "投射标记不能隐式授予空中能力");
            Require(AI_Bird.AllowsDamage(phase, CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneTargets), "箭矢空中能力被拒绝");
            Require(AI_Bird.AllowsDamage(phase, CombatDeliveryCapabilities.AirborneTargets), "MOD 空中能力被拒绝");
        }
        Require(AI_Bird.ResolveHeight(BirdFlightPhase.Ground, 0f, 0.75f, 1.5f) == 0f, "地面高度错误");
        Require(Mathf.Approximately(AI_Bird.ResolveHeight(BirdFlightPhase.TakingOff, 0.375f, 0.75f, 1.5f), 0.75f), "起飞插值错误");
        Require(AI_Bird.ResolveHeight(BirdFlightPhase.Flying, 30f, 0.75f, 1.5f) == 1.5f, "巡航高度错误");
        Require(AI_Bird.ResolveHeight(BirdFlightPhase.Landing, 0.75f, 0.75f, 1.5f) == 0f, "降落未归零");
        Require(RuntimeTreeHabitatIndex.ResolveCapacity(0, 4) == 0, "无树仍有鸟容量");
        Require(RuntimeTreeHabitatIndex.ResolveCapacity(4, 4) == 1, "基础树容量错误");
        Require(RuntimeTreeHabitatIndex.ResolveCapacity(8, 4) == 2, "树容量没有随树量增加");
        var data = new Ex_ModData();
        var state = new AI_Bird.FlightState { Phase = BirdFlightPhase.Flying, Elapsed = 17f, TargetX = 8f, TargetY = -4f, HasTarget = true };
        data.WriteData(state);
        AI_Bird.FlightState restored = data.GetData<AI_Bird.FlightState>();
        Require(restored.Phase == state.Phase && restored.Elapsed == 17f && restored.TargetX == 8f && restored.TargetY == -4f && restored.HasTarget,
            "飞行状态持久化丢失或混入表现高度");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Bird diagnostics: " + message);
    }
    #endregion
}
