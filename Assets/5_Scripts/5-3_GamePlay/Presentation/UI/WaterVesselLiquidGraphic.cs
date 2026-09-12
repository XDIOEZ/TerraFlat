using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>容器剖面的像素水层：按真实容量比例显示高度，以 12 帧每秒绘制轻微波纹；外部 Mask 限制罐内轮廓。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaterVesselLiquidGraphic : MaskableGraphic
{
    [Serializable]
    public struct LiquidStyle
    {
        public string VisualState; // 液体定义的表现状态。
        public Color Body, Surface, Detail; // 水体、水面、悬浮物或泡沫颜色。
        public bool Foam; // 海水泡沫。
    }
    public LiquidStyle[] Styles; // Prefab 配置视觉，不修改液体玩法定义。
    public Vector2 FillRange = new Vector2(20f / 128f, 94f / 128f); // 罐内可用水位的归一化高度。
    private LiquidStyle style;
    private string visualState;
    private float level, targetLevel, nextFrame;
    private int frame;

    /// <summary>接收容器真实数据，打开时直接定位，使用过程中平滑升降。</summary>
    public void SetWater(int amount, int capacity, string id, bool immediate = false)
    {
        float value = (float)amount / capacity;
        bool changedStyle = amount > 0 && id != visualState;
        if (changedStyle)
        {
            int index = Array.FindIndex(Styles, entry => string.Equals(entry.VisualState, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"液体 {id} 未配置容器视觉。");
            style = Styles[index];
            visualState = id;
        }
        if (!immediate && !changedStyle && targetLevel == value) return;
        targetLevel = value;
        if (immediate) level = value;
        SetVerticesDirty();
    }

    /// <summary>只在可见且有水时更新像素波纹。</summary>
    private void Update()
    {
        if (Time.unscaledTime < nextFrame || (level <= 0 && targetLevel <= 0)) return;
        nextFrame = Time.unscaledTime + 1f / 12f;
        level = Mathf.MoveTowards(level, targetLevel, .08f);
        frame++;
        SetVerticesDirty();
    }

    /// <summary>水面分列绘制，脏水带颗粒，淡水带反光，海水带浮沫。</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (level <= 0) return;
        Rect r = rectTransform.rect;
        float pixel = r.height / 128f;
        float bottom = r.yMin + r.height * FillRange.x;
        float surface = Mathf.Lerp(bottom, r.yMin + r.height * FillRange.y, level);
        for (int i = 0; i < 32; i++)
        {
            float x = r.xMin + i * r.width / 32f;
            float top = surface + Mathf.Round(Mathf.Sin(i * .6f + frame * .3f)) * pixel * Mathf.Min(1, level * 12);
            Quad(mesh, x, bottom, r.width / 32f, top - bottom, style.Body);
            Quad(mesh, x, Mathf.Max(bottom, top - pixel), r.width / 32f, Mathf.Min(pixel, top - bottom), style.Surface);
            if (i % 5 == 0)
            {
                float y = style.Foam ? top - pixel : Mathf.Lerp(bottom, top, .3f + .4f * Mathf.Abs(Mathf.Sin(i + frame * .04f)));
                Quad(mesh, x, Mathf.Max(bottom, y), pixel * (style.Foam ? 3 : 1), Mathf.Min(pixel, top - bottom), style.Detail);
            }
        }
    }

    /// <summary>添加一个无贴图像素矩形。</summary>
    private static void Quad(VertexHelper mesh, float x, float y, float width, float height, Color color)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(x, y, 0), color, Vector2.zero);
        mesh.AddVert(new Vector3(x, y + height, 0), color, Vector2.zero);
        mesh.AddVert(new Vector3(x + width, y + height, 0), color, Vector2.zero);
        mesh.AddVert(new Vector3(x + width, y, 0), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
