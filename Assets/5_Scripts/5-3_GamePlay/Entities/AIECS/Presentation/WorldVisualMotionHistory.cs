using Unity.Mathematics;
using UnityEngine;

/// <summary>表现层独享的前后位置与时间，不写回模拟、物理或存档。</summary>
public sealed class WorldVisualMotionHistory
{
    #region 视觉运动快照

    public const float TeleportDistance = 2f;
    public const float PixelGrid = 32f;
    private const float DefaultInterval = 1f / 20f;
    private const float MaxInterval = 0.25f;
    private const float MinInterval = 1f / 120f;

    private Vector3 previous;
    private Vector3 current;
    private double lastChangeTime;
    private float interval;
    public bool HasSnapshot { get; private set; }
    public Vector3 Current => current;

    public void Reset(Vector3 position, double simulationTime)
    {
        previous = current = position;
        lastChangeTime = simulationTime;
        interval = 0f;
        HasSnapshot = true;
    }

    public void Capture(Vector3 position, double simulationTime)
    {
        if (!HasSnapshot || !IsFinite(position) || !IsFinite(current) ||
            !double.IsFinite(simulationTime) || simulationTime < lastChangeTime ||
            (position - current).sqrMagnitude > TeleportDistance * TeleportDistance)
        {
            Reset(position, simulationTime);
            return;
        }
        if ((position - current).sqrMagnitude < 0.0000000001f) return;
        double seconds = simulationTime - lastChangeTime;
        if (seconds <= 0d)
        {
            // 非固定步移动不伪装成可插值的权威 Tick。
            Reset(position, simulationTime);
            return;
        }
        previous = current;
        current = position;
        interval = seconds > MaxInterval ? DefaultInterval : Mathf.Max(MinInterval, (float)seconds);
        lastChangeTime = simulationTime;
    }

    public void CaptureWrapped(Vector2 position, WorldTopologyDomain domain, double simulationTime)
    {
        if (!HasSnapshot)
        {
            Reset(position, simulationTime);
            return;
        }
        float2 delta = domain.ShortestDelta(new float2(current.x, current.y), new float2(position.x, position.y));
        Capture(current + new Vector3(delta.x, delta.y, 0f), simulationTime);
    }

    public Vector3 Evaluate(double renderTime)
    {
        if (!HasSnapshot || interval <= 0f || !double.IsFinite(renderTime)) return current;
        float alpha = Mathf.Clamp01((float)((renderTime - lastChangeTime) / interval));
        return Vector3.LerpUnclamped(previous, current, alpha);
    }

    public static Vector3 PixelAlignTranslation(Vector3 translation, float pixelsPerUnit)
    {
        if (pixelsPerUnit <= 0f || !float.IsFinite(pixelsPerUnit)) return translation;
        return new Vector3(Mathf.Round(translation.x * pixelsPerUnit) / pixelsPerUnit,
            Mathf.Round(translation.y * pixelsPerUnit) / pixelsPerUnit, translation.z);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    #endregion
}
