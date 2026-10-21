using System;
using UnityEngine;

#region 生态权威快照边界
[Serializable]
public struct AiEcologyNetworkActor
{
    public int Guid;
    public string Species;
    public Vector2 Position, Facing;
    public float Hp, MaxHp, ActionElapsed, LiquidDepth, WaterBlend, FlightHeight, FlightCruiseHeight;
    public int Behavior, AttackPhase;
    public byte Dead, FlightAirborne, Moving;
}

// 网络层只搬运已结算的表现快照，不拥有 ECS 模拟或掉落规则。
public static class AiEcologyNetworkBridge
{
    public static Func<string, Vector2, float, AiEcologyNetworkActor[]> Capture;
    public static Action<string, AiEcologyNetworkActor[]> Apply;
    public static Action Clear;
}
#endregion
