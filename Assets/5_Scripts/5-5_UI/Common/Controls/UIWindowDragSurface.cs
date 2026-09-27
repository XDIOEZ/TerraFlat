using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 窗口的透明拖动命中面，只在标题栏和边框接收射线。
/// 命中范围以实际窗口矩形为准，避免全屏安全区节点挡住背包、合成槽和快捷栏。
/// </summary>
public sealed class UIWindowDragSurface : MaskableGraphic
{
    #region 命中范围

    /// <summary>实际可见窗口的矩形。</summary>
    [SerializeField] private RectTransform windowRect;

    /// <summary>标题栏可拖动高度，单位为窗口本地坐标。</summary>
    [SerializeField] private float headerHeight = 78f;

    /// <summary>外框可拖动宽度，单位为窗口本地坐标。</summary>
    [SerializeField] private float borderWidth = 12f;

    /// <summary>配置窗口的拖动范围。</summary>
    public void Configure(RectTransform bounds, float header = 78f, float border = 12f)
    {
        windowRect = bounds;
        headerHeight = header;
        borderWidth = border;
    }

    /// <summary>只让标题栏和外框进入 UI 射线结果。</summary>
    public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
    {
        return base.Raycast(screenPoint, eventCamera) && IsDragHit(screenPoint, eventCamera);
    }

    /// <summary>判断当前指针是否位于窗口的拖动区域。</summary>
    public bool IsDragHit(Vector2 screenPoint, Camera eventCamera)
    {
        if (windowRect == null ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(windowRect, screenPoint, eventCamera, out Vector2 point))
            return false;

        Rect rect = windowRect.rect;
        if (!rect.Contains(point))
            return false;

        return point.y >= rect.yMax - headerHeight ||
               point.x <= rect.xMin + borderWidth ||
               point.x >= rect.xMax - borderWidth ||
               point.y <= rect.yMin + borderWidth;
    }

    /// <summary>生成透明网格供 GraphicRaycaster 排序，不绘制可见底板。</summary>
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = rectTransform.rect;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = Color.clear;
        vertex.position = new Vector2(rect.xMin, rect.yMin);
        helper.AddVert(vertex);
        vertex.position = new Vector2(rect.xMin, rect.yMax);
        helper.AddVert(vertex);
        vertex.position = new Vector2(rect.xMax, rect.yMax);
        helper.AddVert(vertex);
        vertex.position = new Vector2(rect.xMax, rect.yMin);
        helper.AddVert(vertex);
        helper.AddTriangle(0, 1, 2);
        helper.AddTriangle(2, 3, 0);
    }

    #endregion
}
