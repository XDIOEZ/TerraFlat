using System;
using UnityEngine;

public static partial class MachineWorld
{
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
