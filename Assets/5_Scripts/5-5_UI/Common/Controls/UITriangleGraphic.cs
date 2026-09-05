using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 可随 UI 缩放的实心三角图标，默认指向左侧并填满 RectTransform。
/// 用于抽屉等方向开关，通过节点旋转切换方向，无需贴图或依赖字体字形。
/// </summary>
[DisallowMultipleComponent]
public sealed class UITriangleGraphic : MaskableGraphic
{
    #region 图形生成

    /// <summary>按当前矩形生成左向三角形，颜色和遮罩沿用 uGUI Graphic。</summary>
    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = rectTransform.rect;
        vertexHelper.AddVert(new Vector3(rect.xMin, rect.center.y), color, Vector2.zero);
        vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
        vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
        vertexHelper.AddTriangle(0, 1, 2);
    }

    #endregion
}
