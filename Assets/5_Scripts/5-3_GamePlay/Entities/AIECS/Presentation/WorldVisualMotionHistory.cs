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
        if ((position - current).sqrMagnitude < 0.0000000001f)
        {
            // 静止步也更新采样时间，避免待机后首次移动按数个Tick的跨度缓慢补间。
            if (simulationTime > lastChangeTime)
            {
                previous = current;
                lastChangeTime = simulationTime;
                interval = 0f;
            }
            return;
        }
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
        => CaptureWrapped(new Vector3(position.x, position.y, 0f), domain, simulationTime);

    public void CaptureWrapped(Vector3 position, WorldTopologyDomain domain, double simulationTime)
    {
        if (!HasSnapshot)
        {
            Reset(position, simulationTime);
            return;
        }
        float2 delta = domain.ShortestDelta(new float2(current.x, current.y), new float2(position.x, position.y));
        Capture(new Vector3(current.x + delta.x, current.y + delta.y, position.z), simulationTime);
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

/// <summary>纯值姿态快照；读显示姿态不会修改源 Transform 或推进历史。</summary>
public readonly struct WorldVisualPose
{
    #region 姿态值
    public readonly Vector3 Position, Scale;
    public readonly Quaternion Rotation;
    public WorldVisualPose(Vector3 position, Quaternion rotation, Vector3 scale)
    { Position = position; Rotation = rotation; Scale = scale; }
    public static WorldVisualPose ReadLocal(Transform source) =>
        new(source.localPosition, source.localRotation, source.localScale);
    public static WorldVisualPose ReadWorld(Transform source) =>
        new(source.position, source.rotation, source.lossyScale);
    public Matrix4x4 Matrix => Matrix4x4.TRS(Position, Rotation, Scale);
    public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 x = matrix.MultiplyVector(Vector3.right * bounds.extents.x);
        Vector3 y = matrix.MultiplyVector(Vector3.up * bounds.extents.y);
        Vector3 z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
        Vector3 extents = new(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extents * 2f);
    }
    public bool Matches(in WorldVisualPose other) => Position.Equals(other.Position) && Scale.Equals(other.Scale) &&
        Rotation.Equals(other.Rotation);
    public static WorldVisualPose Interpolate(in WorldVisualPose from, in WorldVisualPose to, float alpha) =>
        new(Vector3.LerpUnclamped(from.Position, to.Position, alpha),
            Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, alpha),
            Vector3.LerpUnclamped(from.Scale, to.Scale, alpha));
    #endregion
}

/// <summary>只在模拟提交时采样，任意数量的渲染帧仅做只读插值。</summary>
public sealed class WorldVisualPoseHistory
{
    #region 逻辑提交与只读表现
    private WorldVisualPose previous, current;
    private double sampledAt, interval;
    public bool HasSnapshot { get; private set; }
    public WorldVisualPose Current => current;
    public double SampledAt => sampledAt;

    public void Reset(in WorldVisualPose pose, double simulationTime)
    {
        previous = current = pose;
        sampledAt = simulationTime;
        interval = 0d;
        HasSnapshot = true;
    }

    public void Capture(in WorldVisualPose pose, double simulationTime,
        float discontinuityDistance = WorldVisualMotionHistory.TeleportDistance)
    {
        if (!HasSnapshot || simulationTime < sampledAt ||
            (pose.Position - current.Position).sqrMagnitude > discontinuityDistance * discontinuityDistance)
        { Reset(pose, simulationTime); return; }
        if (simulationTime == sampledAt)
        {
            if (!pose.Matches(current)) Reset(pose, simulationTime);
            return;
        }
        previous = current;
        current = pose;
        interval = simulationTime - sampledAt;
        sampledAt = simulationTime;
    }

    public WorldVisualPose Evaluate(double presentationTime)
    {
        if (!HasSnapshot || interval <= 0d || !double.IsFinite(presentationTime)) return current;
        float alpha = Mathf.Clamp01((float)((presentationTime - sampledAt) / interval));
        return WorldVisualPose.Interpolate(previous, current, alpha);
    }

    public WorldVisualPose Evaluate(in WorldVisualPose livePose, double presentationTime)
    {
        // 各通道独立判定，逐帧摆动不能意外关闭仍由 Tick 驱动的位置插值。
        if (!HasSnapshot) return livePose;
        WorldVisualPose displayed = Evaluate(presentationTime);
        return new WorldVisualPose(livePose.Position.Equals(current.Position) ? displayed.Position : livePose.Position,
            livePose.Rotation.Equals(current.Rotation) ? displayed.Rotation : livePose.Rotation,
            livePose.Scale.Equals(current.Scale) ? displayed.Scale : livePose.Scale);
    }
    #endregion
}
