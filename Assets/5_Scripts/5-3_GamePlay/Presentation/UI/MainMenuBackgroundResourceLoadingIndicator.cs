using UnityEngine;
using UnityEngine.UI;

/// <summary>主菜单左下角的轻量加载圈：只观察后台世界资源预加载，不参与加载调度。</summary>
[DisallowMultipleComponent]
public sealed class MainMenuBackgroundResourceLoadingIndicator : MaskableGraphic
{
    #region 加载状态与旋转

    private const float RotationDegreesPerSecond = 180f;
    private const int RingSegments = 24;
    private const int TrailSegments = 20;
    private bool isVisible;

    protected override void OnEnable()
    {
        base.OnEnable();
        isVisible = false;
        canvasRenderer.SetAlpha(0f);
        raycastTarget = false;
    }

    private void Update()
    {
        GameRes resources = GameRes.ExistingInstance;
        bool shouldShow = resources != null && resources.IsBackgroundWorldResourcePreloadInProgress;
        if (shouldShow != isVisible)
        {
            isVisible = shouldShow;
            canvasRenderer.SetAlpha(shouldShow ? 1f : 0f);
        }

        if (shouldShow)
            rectTransform.Rotate(0f, 0f, -RotationDegreesPerSecond * Time.unscaledDeltaTime);
    }

    #endregion

    #region 加载圈绘制

    /// <summary>绘制不依赖贴图的渐隐圆弧，旋转时不逐帧修改顶点。</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float innerRadius = Mathf.Max(0f, outerRadius - 3f);
        float step = Mathf.PI * 2f / RingSegments;

        for (int i = 0; i < TrailSegments; i++)
        {
            float start = i * step;
            float end = start + step * 0.82f;
            Color32 tint = color;
            tint.a = (byte)Mathf.RoundToInt(color.a * 255f * Mathf.Lerp(0.12f, 1f, i / (float)(TrailSegments - 1)));

            int index = mesh.currentVertCount;
            mesh.AddVert(center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * innerRadius, tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * outerRadius, tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * outerRadius, tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * innerRadius, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }

    #endregion
}
