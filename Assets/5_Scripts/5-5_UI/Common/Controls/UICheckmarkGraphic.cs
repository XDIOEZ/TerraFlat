using UnityEngine;
using UnityEngine.UI;

/// <summary>不依赖字体或额外贴图的复选勾，随 Toggle.graphic 的透明度显示。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UICheckmarkGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 Point(float x, float y) => new(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
        float thickness = Mathf.Min(rect.width, rect.height) * 0.14f;
        AddStroke(helper, Point(0.14f, 0.49f), Point(0.39f, 0.24f), thickness);
        AddStroke(helper, Point(0.39f, 0.24f), Point(0.87f, 0.82f), thickness);
    }

    private void AddStroke(VertexHelper helper, Vector2 start, Vector2 end, float thickness)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);
        int first = helper.currentVertCount;
        helper.AddVert(start - normal, color, Vector2.zero);
        helper.AddVert(start + normal, color, Vector2.zero);
        helper.AddVert(end + normal, color, Vector2.zero);
        helper.AddVert(end - normal, color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first + 2, first + 3, first);
    }
}
