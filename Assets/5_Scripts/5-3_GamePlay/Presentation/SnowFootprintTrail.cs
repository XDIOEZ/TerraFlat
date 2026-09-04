using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 雪地脚印表现组件。组件只记录移动轨迹，默认完整保留 60 秒后再按点龄渐隐；
/// LineRenderer 的材质可直接替换为脚印纹理，因此不会把脚印贴图写死在逻辑中。
/// </summary>
[DisallowMultipleComponent]
public sealed class SnowFootprintTrail : MonoBehaviour
{
    private const float FadeDuration = 3f;

    private struct FootprintPoint
    {
        public Vector3 Position;
        public float Time;

        public FootprintPoint(Vector3 position, float time)
        {
            Position = position;
            Time = time;
        }
    }

    [SerializeField, Min(0.1f)] private float lifetime = 60f;
    [SerializeField, Min(0.01f)] private float minimumPointDistance = 0.18f;
    [SerializeField, Min(0.01f)] private float lineWidth = 0.12f;
    [SerializeField] private Material footprintMaterial;

    private readonly List<FootprintPoint> points = new();
    private LineRenderer lineRenderer;
    private bool surfaceActive;
    private bool hasLastPoint;
    private Vector3 lastPoint;

    /// <summary>设置脚印完整保留时间。</summary>
    public void ConfigureLifetime(float seconds)
    {
        lifetime = Mathf.Max(0.1f, seconds);
    }

    /// <summary>启用或停用脚印采样；停用时保留已有脚印并继续自然衰减。</summary>
    public void SetSurfaceActive(bool active)
    {
        surfaceActive = active;
        EnsureRenderer();
        if (!active)
        {
            hasLastPoint = false;
            lineRenderer.enabled = points.Count > 0;
            return;
        }

        lineRenderer.enabled = true;
    }

    /// <summary>记录雪地移动轨迹，并持续清理超过保留期的最旧脚印。</summary>
    private void LateUpdate()
    {
        if (!surfaceActive && points.Count == 0)
            return;

        EnsureRenderer();
        float now = Time.time;
        if (surfaceActive && (!hasLastPoint ||
            (transform.position - lastPoint).sqrMagnitude >= minimumPointDistance * minimumPointDistance)
           )
        {
            points.Add(new FootprintPoint(transform.position, now));
            lastPoint = transform.position;
            hasLastPoint = true;
        }

        float oldestAllowedTime = now - lifetime - FadeDuration;
        int firstValidIndex = 0;
        while (firstValidIndex < points.Count && points[firstValidIndex].Time < oldestAllowedTime)
            firstValidIndex++;

        if (firstValidIndex > 0)
            points.RemoveRange(0, firstValidIndex);

        RebuildLine(now);
        if (!surfaceActive && points.Count == 0)
            lineRenderer.enabled = false;
    }

    /// <summary>确保脚印使用独立的世界空间 LineRenderer 表现。</summary>
    private void EnsureRenderer()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                GameObject lineObject = new("SnowFootprints");
                lineObject.transform.SetParent(transform, false);
                lineRenderer = lineObject.AddComponent<LineRenderer>();
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.TransformZ;
            lineRenderer.textureMode = LineTextureMode.Tile;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 0;
            lineRenderer.sortingOrder = -1;
        }

        lineRenderer.widthMultiplier = lineWidth;
        if (footprintMaterial != null)
        {
            lineRenderer.sharedMaterial = footprintMaterial;
        }
        else if (lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                lineRenderer.sharedMaterial = new Material(shader)
                {
                    color = new Color(0.7f, 0.78f, 0.82f, 0.8f),
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }
    }

    /// <summary>根据每个轨迹点的年龄重建位置与末段渐隐透明度。</summary>
    private void RebuildLine(float now)
    {
        if (points.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        lineRenderer.positionCount = points.Count;
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[points.Count];
        GradientColorKey[] colorKeys =
        {
            new(Color.white, 0f),
            new(Color.white, 1f)
        };

        for (int i = 0; i < points.Count; i++)
        {
            FootprintPoint point = points[i];
            float fadeAge = Mathf.Max(0f, now - point.Time - lifetime);
            float fade01 = Mathf.Clamp01(fadeAge / FadeDuration);
            float alpha = Mathf.Pow(1f - fade01, 0.7f);
            lineRenderer.SetPosition(i, point.Position + Vector3.back * 0.02f);
            alphaKeys[i] = new GradientAlphaKey(
                alpha,
                points.Count == 1 ? 0f : (float)i / (points.Count - 1));
        }

        Gradient gradient = new();
        gradient.SetKeys(colorKeys, alphaKeys);
        lineRenderer.colorGradient = gradient;
    }

    /// <summary>实体禁用时彻底释放当前运行时轨迹状态。</summary>
    private void OnDisable()
    {
        surfaceActive = false;
        points.Clear();
        hasLastPoint = false;
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }
    }
}
