using System;
using FlatWorld.Localization;
using UnityEngine;

public static partial class MachineWorld
{
    #region 输送带手动朝向
    private static readonly string[] conveyorDirections = { "东", "北", "西", "南" };
    /// <summary>切换当前段的固定连接形状与方向，完成一轮后恢复自动连接。</summary>
    public static bool CycleConveyorOrientation(MachineEntity node)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || !Contains(node) ||
            node.Definition.Transport == null) return false;
        WakeForInteraction(node);
        RebuildGraphsIfDirty();
        node.State.ConveyorMode = (node.State.ConveyorMode + 1) % 13;
        if (node.State.ConveyorMode != 0)
            node.State.RotationQuarterTurns = ConveyorPath.FromManualMode(node.State.ConveyorMode).Output;
        TopologyChanged(node);
        RebuildGraphsIfDirty();
        GetOrCreateInteractionTarget(node)?.RefreshPanel();
        return true;
    }

    public static string GetConveyorConnectionStatus(MachineEntity node)
    {
        ConveyorPath route = node.ConveyorRoute;
        return FlatWorldLocalizationService.GetUiFormat(" · {0}：{1}（{2} → {3}）",
            FlatWorldLocalizationService.GetUiText(node.ConveyorMode == 0 ? "自动连接" : "手动连接"),
            FlatWorldLocalizationService.GetUiText(route.Curved ? "拐角" : "直线"),
            FlatWorldLocalizationService.GetUiText(conveyorDirections[route.Input]),
            FlatWorldLocalizationService.GetUiText(conveyorDirections[route.Output]));
    }
    #endregion

    #region 输送带表面推动
    /// <summary>只采样带面速度，角色位移与阻挡仍交给自身刚体，不修改机械状态。</summary>
    public static Vector2 SampleConveyorVelocity(Vector2 position, float seconds)
    {
        if (!MachineDefinition.Positive(seconds)) return Vector2.zero;
        MachineEntity belt = GetTransportAt(position);
        if (belt == null || !belt.Active || belt.SpeedRpm <= 0f) return Vector2.zero;
        MachineTransportDefinition transport = belt.Definition.Transport;
        MachineTransportVisualDefinition visual = transport.Visual;
        float speed = transport.Speed * GetWorkEfficiency(belt);
        if (!MachineDefinition.Positive(speed)) return Vector2.zero;
        float halfWidth = Mathf.Min(transport.HalfWidth,
            visual.CanvasHeight * (visual.SurfaceUvMax - visual.SurfaceUvMin) * .5f);
        Vector2 center = (Vector2)belt.Cell + Vector2.one * .5f;
        Vector2 offset = WorldTopologyRuntime.ShortestDelta(center, position);
        // 玩家保留站立的侧向位置，允许自行横向走出带面，不像掉落物一样向中间收拢。
        return belt.ConveyorRoute.TryMove(offset, speed * seconds * Mathf.Sign(belt.Rpm),
            halfWidth, out Vector2 advanced, centerLateral: false)
            ? (advanced - offset) / seconds : Vector2.zero;
    }
    #endregion

    #region 输送带表现
    public static ConveyorPath GetConveyorPreviewPath(Vector2Int cell, int rotation, bool autoConnect)
    {
        if (graph == null) return ConveyorPath.Straight(rotation);
        RebuildGraphsIfDirty();
        return graph.SelectConveyorPath(cell, rotation, autoConnect);
    }

    /// <summary>以实际每秒输送距离积分，停转与反转不会因 RPM 换算跳相。</summary>
    private static bool UpdateConveyorVisualSpeed(MachineEntity node)
    {
        if (node.Definition.Transport == null) return false;
        float speed = node.Definition.Transport.Speed * GetWorkEfficiency(node) *
                      Math.Sign(node.Rpm) * node.ConveyorRoute.CanonicalSign;
        if (Mathf.Abs(speed - node.ConveyorVisualSpeed) < .00001f &&
            Math.Sign(speed) == Math.Sign(node.ConveyorVisualSpeed)) return false;
        float now = Time.time;
        node.ConveyorVisualPhase = Mathf.Repeat(node.ConveyorVisualPhase +
            (now - node.ConveyorVisualTime) * node.ConveyorVisualSpeed, 1f);
        node.ConveyorVisualTime = now; node.ConveyorVisualSpeed = speed;
        return true;
    }
    #endregion
}
