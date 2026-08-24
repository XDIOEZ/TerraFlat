using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 雪地脚印表现组件。组件只记录移动轨迹并按点龄渐隐，默认保留 10 秒；
/// LineRenderer 的材质可直接替换为脚印纹理，因此不会把脚印贴图写死在逻辑中。
/// </summary>
[DisallowMultipleComponent]
public sealed class SnowFootprintTrail : MonoBehaviour
{
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

    [SerializeField, Min(0.1f)] private float lifetime = 10f;
    [SerializeField, Min(0.01f)] private float minimumPointDistance = 0.18f;
    [SerializeField, Min(0.01f)] private float lineWidth = 0.12f;
    [SerializeField] private Material footprintMaterial;

    private readonly List<FootprintPoint> points = new();
    private LineRenderer lineRenderer;
    private bool surfaceActive;
    private bool hasLastPoint;
    private Vector3 lastPoint;

    /// <summary>设置脚印持续时间。</summary>
    public void ConfigureLifetime(float seconds)
    {
        lifetime = Mathf.Max(0.1f, seconds);
    }

    /// <summary>启用或停用当前雪地脚印轨迹。</summary>
    public void SetSurfaceActive(bool active)
    {
        surfaceActive = active;
        EnsureRenderer();
        if (!active)
        {
            points.Clear();
            hasLastPoint = false;
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
    }

    private void LateUpdate()
    {
        if (!surfaceActive)
            return;

        EnsureRenderer();
        float now = Time.time;
        if (!hasLastPoint ||
            (transform.position - lastPoint).sqrMagnitude >= minimumPointDistance * minimumPointDistance)
        {
            points.Add(new FootprintPoint(transform.position, now));
            lastPoint = transform.position;
            hasLastPoint = true;
        }

        float oldestAllowedTime = now - lifetime;
        int firstValidIndex = 0;
        while (firstValidIndex < points.Count && points[firstValidIndex].Time < oldestAllowedTime)
            firstValidIndex++;

        if (firstValidIndex > 0)
            points.RemoveRange(0, firstValidIndex);

        RebuildLine(now);
    }

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
            float age01 = Mathf.Clamp01((now - point.Time) / lifetime);
            float alpha = Mathf.Pow(1f - age01, 0.7f);
            lineRenderer.SetPosition(i, point.Position + Vector3.back * 0.02f);
            alphaKeys[i] = new GradientAlphaKey(
                alpha,
                points.Count == 1 ? 0f : (float)i / (points.Count - 1));
        }

        Gradient gradient = new();
        gradient.SetKeys(colorKeys, alphaKeys);
        lineRenderer.colorGradient = gradient;
    }

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
