using System.Collections.Generic;
using UnityEngine;

/// <summary>只识别触屏甩动轨迹，物品转移继续交给库存事务。</summary>
public sealed class InventoryFlickGesture
{
    #region 轨迹与阈值
    public const float DefaultMinimumDistance = 64f;
    public const float DefaultMinimumSpeed = 800f;
    private const double VelocityWindow = 0.1d;
    private const double ReleaseWindow = 0.04d;
    private readonly List<(Vector2 Position, double Time)> samples = new(32);
    private Vector2 origin;
    private float scale;
    #endregion

    #region 手势生命周期
    public void Begin(Vector2 screenPosition, double time, float canvasScale)
    {
        Reset();
        scale = Mathf.Max(0.01f, canvasScale);
        origin = screenPosition / scale;
        samples.Add((origin, time));
    }

    public void Record(Vector2 screenPosition, double time)
    {
        if (samples.Count == 0) return;
        if (time < samples[^1].Time)
        {
            Reset();
            return;
        }

        var sample = (Position: screenPosition / scale, Time: time);
        if (time == samples[^1].Time) samples[^1] = sample;
        else samples.Add(sample);

        // 留住窗口边界前的一个采样，插值后再测速，避免帧率改变判定。
        while (samples.Count > 2 && samples[1].Time < time - VelocityWindow)
            samples.RemoveAt(0);
    }

    public void Reset() => samples.Clear();
    #endregion

    #region 松手判定
    public bool IsFlick(Vector2 screenPosition, double time,
        float minimumDistance = DefaultMinimumDistance, float minimumSpeed = DefaultMinimumSpeed)
    {
        Record(screenPosition, time);
        if (samples.Count < 2 || minimumDistance <= 0f || minimumSpeed <= 0f) return false;
        Vector2 displacement = samples[^1].Position - origin;
        if (displacement.magnitude < minimumDistance) return false;

        MeasureWindow(VelocityWindow, out Vector2 recent, out float path, out double duration);
        if (duration < 0.01d || recent.magnitude / duration < minimumSpeed ||
            recent.magnitude < path * 0.8f || Vector2.Dot(displacement, recent) <= 0f)
            return false;

        // 末段仍需保持速度，阻止快拖后停住或轻微手抖被认成甩动。
        MeasureWindow(ReleaseWindow, out Vector2 release, out _, out double releaseDuration);
        return releaseDuration >= 0.01d && release.magnitude / releaseDuration >= minimumSpeed * 0.5f &&
               Vector2.Dot(recent, release) > 0f;
    }

    private void MeasureWindow(double window, out Vector2 displacement, out float path, out double duration)
    {
        double startTime = System.Math.Max(samples[0].Time, samples[^1].Time - window);
        int index = 0;
        while (index + 1 < samples.Count && samples[index + 1].Time <= startTime) index++;
        Vector2 start = samples[index].Position;
        if (index + 1 < samples.Count)
        {
            var next = samples[index + 1];
            float fraction = (float)((startTime - samples[index].Time) / (next.Time - samples[index].Time));
            start = Vector2.Lerp(start, next.Position, fraction);
        }

        path = 0f;
        Vector2 previous = start;
        for (int i = index + 1; i < samples.Count; i++)
        {
            path += Vector2.Distance(previous, samples[i].Position);
            previous = samples[i].Position;
        }
        displacement = samples[^1].Position - start;
        duration = samples[^1].Time - startTime;
    }
    #endregion
}
