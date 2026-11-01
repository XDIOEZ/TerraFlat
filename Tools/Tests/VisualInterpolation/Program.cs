using System;
using UnityEngine;
using Unity.Mathematics;

internal static class Program
{
    private static int checks;

    private static void Near(float actual, float expected, string message)
    {
        if (Math.Abs(actual - expected) > 0.0002f)
            throw new Exception($"{message}: {actual} != {expected}");
        checks++;
    }

    public static void Main()
    {
        var history = new WorldVisualMotionHistory();
        history.Reset(Vector3.zero, 0d);
        history.Capture(Vector3.right, .05d);
        foreach (int fps in new[] { 60, 120, 240 })
        {
            for (int frame = 0; frame <= fps / 20; frame++)
                Near(history.Evaluate(.05d + (double)frame / fps).x, (float)frame * 20f / fps,
                    $"20TPS / {fps}FPS [{frame}]");
        }
        Near(history.Evaluate(.075d).x, .5f, "中间帧");
        Near(history.Evaluate(.075d).x, .5f, "暂停时帧时间不推进");
        history.Capture(new Vector3(2f, 0f, 0f), .1d);
        Near(history.Evaluate(.125d).x, 1.5f, "连续低频逻辑步");
        history.Capture(new Vector3(20f, 0f, 0f), .15d);
        Near(history.Evaluate(.15d).x, 20f, "瞬移取消历史");
        history.Capture(new Vector3(22.5f, 0f, 0f), .2d);
        Near(history.Evaluate(.2d).x, 22.5f, "短距离传送阈值");
        history.Reset(new Vector3(-3f, 0f, 0f), .2d);
        Near(history.Evaluate(.225d).x, -3f, "复用重置");
        history.Capture(new Vector3(-2f, 0f, 0f), .2d);
        Near(history.Evaluate(.225d).x, -2f, "非固定步移动不插值");
        history.Capture(new Vector3(-1f, 0f, 0f), .25d);
        Near(history.Evaluate(.2d).x, -2f, "时间不倒流");
        var wrapped = new WorldVisualMotionHistory();
        wrapped.Reset(new Vector3(99f, 0f, 0f), 1d);
        wrapped.CaptureWrapped(new Vector2(0f, 0f), new WorldTopologyDomain(), 1.05d);
        Near(wrapped.Evaluate(1.075d).x, 99.5f, "环绕接缝最短路径");
        Near(WorldVisualMotionHistory.PixelAlignTranslation(new Vector3(.12f, 0f, 0f), 32f).x,
            .125f, "像素网格");
        Console.WriteLine($"PASS {checks} checks: 20 TPS / 60, 120, 240 FPS + teleport, pooling, pause, wrap, pixel");
    }
}

// 仅为编译并执行生产插值类提供最小的 Unity 数学替身，不复制生产算法。
public readonly struct WorldTopologyDomain
{
    public float2 ShortestDelta(float2 from, float2 to)
    {
        float dx = to.x - from.x, dy = to.y - from.y;
        if (dx > 50) dx -= 100;
        if (dx < -50) dx += 100;
        if (dy > 50) dy -= 100;
        if (dy < -50) dy += 100;
        return new float2(dx, dy);
    }
}

namespace Unity.Mathematics
{
    public readonly struct float2
    {
        public readonly float x, y;
        public float2(float x, float y) { this.x = x; this.y = y; }
    }
}

namespace UnityEngine
{
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static implicit operator Vector3(Vector2 value) => new(value.x, value.y, 0f);
    }

    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 right => new(1f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t)
            => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float Max(float left, float right) => Math.Max(left, right);
        public static float Round(float value) => (float)Math.Round(value);
    }
}
