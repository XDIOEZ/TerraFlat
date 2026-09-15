using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>容器剖面的像素液体层：以 12 帧每秒追随真实容量并绘制波纹；黏稠度独立控制缓动和光泽，外部 Mask 限制容器轮廓。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaterVesselLiquidGraphic : MaskableGraphic
{
    [Serializable]
    public struct LiquidStyle
    {
        public string VisualState; // 液体定义的表现状态。
        public Color Body, Surface, Detail; // 主水体、水面、颗粒/泡沫颜色。
        public Color Deep; // 浑浊液体底部与沉淀的深色。
        [Range(0f, 1f)] public float Murkiness; // 浑浊度：控制上下分层与倾倒液流厚重感。
        [Range(0f, 0.25f)] public float Sediment; // 底部沉淀占当前水深的比例。
        [Range(0f, 1f)] public float SurfaceDebris; // 水面断续污膜/漂浮物强度。
        [Range(0f, 1f)] public float SuspendedParticles; // 水体内悬浮颗粒密度。
        [Range(0f, 1f)] public float Viscosity; // 视觉黏稠度：降低波速与扰动，增加缓慢回落和光泽；零保持水的原有表现。
        public bool Foam; // 海水泡沫。
    }
    public LiquidStyle[] Styles; // Prefab 配置视觉，不修改液体玩法定义。
    public Vector2 FillRange = new Vector2(20f / 128f, 94f / 128f); // 罐内可用水位的归一化高度。
    private LiquidStyle style;
    private string visualState;
    private float level, targetLevel, nextFrame;
    private float agitation; // 来回摇晃产生的额外水面波动，随时间自然衰减。
    private int frame;

    public Color CurrentBodyColor => style.Body;
    public Color CurrentSurfaceColor => style.Surface;
    public Color CurrentDetailColor => style.Detail;
    public float CurrentMurkiness => style.Murkiness;
    public float CurrentViscosity => style.Viscosity;

    /// <summary>接收容器真实数据，打开时直接定位，使用过程中平滑升降。</summary>
    public void SetWater(float amount, int capacity, string id, bool immediate = false)
    {
        float value = capacity > 0 ? Mathf.Clamp01(amount / capacity) : 0f;
        bool changedStyle = amount > 0 && id != visualState;
        if (changedStyle)
        {
            int index = Array.FindIndex(Styles, entry => string.Equals(entry.VisualState, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException($"液体 {id} 未配置容器视觉。");
            style = Styles[index];
            visualState = id;
            agitation = 0f;
        }
        if (!immediate && !changedStyle && targetLevel == value) return;
        targetLevel = value;
        if (immediate) level = value;
        SetVerticesDirty();
    }

    /// <summary>把罐体角速度转成临时水面扰动；空容器不产生无意义波纹。</summary>
    public void AddAgitation(float normalizedImpulse)
    {
        if (level <= 0f && targetLevel <= 0f)
            return;

        agitation = Mathf.Clamp01(Mathf.Max(agitation, normalizedImpulse * Mathf.Lerp(1f, 0.3f, style.Viscosity)));
    }

    /// <summary>只在可见且有水时更新像素波纹。</summary>
    private void Update()
    {
        agitation = Mathf.MoveTowards(agitation, 0f, Time.unscaledDeltaTime * Mathf.Lerp(1.6f, 3.2f, style.Viscosity));
        if (Time.unscaledTime < nextFrame || (level <= 0 && targetLevel <= 0)) return;
        nextFrame = Time.unscaledTime + 1f / 12f;
        level = Mathf.MoveTowards(level, targetLevel, .08f * Mathf.Lerp(1f, 0.45f, style.Viscosity));
        frame++;
        SetVerticesDirty();
    }

    /// <summary>按液体视觉参数绘制水体；浑浊液体额外叠加深浅层、沉淀、悬浮颗粒和断续污膜。</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (level <= 0) return;
        Rect r = rectTransform.rect;
        float pixel = r.height / 128f;
        float bottom = r.yMin + r.height * FillRange.x;
        float surface = Mathf.Lerp(bottom, r.yMin + r.height * FillRange.y, level);
        float wavePixels = Mathf.Lerp(1f, 5f, agitation) * Mathf.Min(1f, level * 12f) *
                           Mathf.Lerp(1f, 0.72f, style.Murkiness) * Mathf.Lerp(1f, 0.45f, style.Viscosity);
        for (int i = 0; i < 32; i++)
        {
            float x = r.xMin + i * r.width / 32f;
            float wave = GetWave(i);
            float top = surface + Mathf.Round(wave * wavePixels) * pixel;
            DrawBodyColumn(mesh, x, bottom, top, r.width / 32f);
            Quad(mesh, x, Mathf.Max(bottom, top - pixel), r.width / 32f, Mathf.Min(pixel, top - bottom), style.Surface);

            if (style.Foam && i % 5 == 0)
            {
                Quad(mesh, x, Mathf.Max(bottom, top - pixel), pixel * 3f, Mathf.Min(pixel, top - bottom), style.Detail);
            }
            else if (style.Murkiness <= 0.01f && style.Viscosity <= 0.01f && i % 5 == 0)
            {
                float y = Mathf.Lerp(bottom, top, .3f + .4f * Mathf.Abs(Mathf.Sin(i + frame * .04f)));
                Quad(mesh, x, Mathf.Max(bottom, y), pixel, Mathf.Min(pixel, top - bottom), style.Detail);
            }
        }

        DrawSediment(mesh, r, bottom, surface, pixel);
        DrawSuspendedParticles(mesh, r, bottom, surface, pixel);
        DrawSurfaceDebris(mesh, r, bottom, surface, wavePixels, pixel);
        DrawViscousHighlights(mesh, r, bottom, surface, pixel);
    }

    /// <summary>浑浊或黏稠液体使用分层色带表现深度；是否出现泥沙仍由独立的沉淀参数决定。</summary>
    private void DrawBodyColumn(VertexHelper mesh, float x, float bottom, float top, float width)
    {
        float height = top - bottom;
        if (height <= 0f)
            return;

        if (style.Murkiness <= 0.01f && style.Viscosity <= 0.01f)
        {
            Quad(mesh, x, bottom, width, height, style.Body);
            return;
        }

        Color deep = style.Deep.a > 0f ? style.Deep : style.Detail;
        const int bands = 6;
        for (int band = 0; band < bands; band++)
        {
            float from = band / (float)bands;
            float to = (band + 1) / (float)bands;
            Color bandColor = Color.Lerp(deep, style.Body, Mathf.Pow((from + to) * 0.5f, 0.72f));
            Quad(mesh, x, bottom + height * from, width, height * (to - from) + 0.01f, bandColor);
        }
    }

    /// <summary>底部沉淀使用不规则上沿，和悬浮水体明确分层。</summary>
    private void DrawSediment(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
    {
        if (style.Sediment <= 0.001f || surface <= bottom)
            return;

        float waterHeight = surface - bottom;
        float baseHeight = Mathf.Max(pixel * 2f, waterHeight * style.Sediment);
        Color sedimentColor = style.Deep.a > 0f ? style.Deep : style.Detail;
        const int columns = 20;
        float width = r.width / columns;
        for (int i = 0; i < columns; i++)
        {
            float irregular = 0.72f + Hash01(i * 17 + 5) * 0.5f;
            float height = Mathf.Min(waterHeight, baseHeight * irregular);
            Quad(mesh, r.xMin + i * width, bottom, width + 0.01f, height, sedimentColor);
        }
    }

    /// <summary>稳定种子的悬浮颗粒缓慢漂移，避免每帧随机闪烁，同时在摇晃时明显翻动。</summary>
    private void DrawSuspendedParticles(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
    {
        if (style.SuspendedParticles <= 0.01f || surface - bottom <= pixel * 3f)
            return;

        int count = Mathf.RoundToInt(Mathf.Lerp(5f, 22f, style.SuspendedParticles) * Mathf.Clamp01(level * 1.5f));
        float sedimentTop = bottom + (surface - bottom) * style.Sediment;
        float minY = Mathf.Min(surface - pixel * 2f, sedimentTop + pixel);
        float maxY = surface - pixel * 2f;
        if (maxY <= minY)
            return;

        Color particleColor = style.Detail;
        particleColor.a *= 0.78f;
        for (int i = 0; i < count; i++)
        {
            float seedX = Hash01(i * 37 + 11);
            float seedY = Hash01(i * 53 + 29);
            float drift = Mathf.Sin(frame * 0.09f + i * 1.73f) * pixel * Mathf.Lerp(0.45f, 2.1f, agitation);
            float rise = Mathf.Cos(frame * 0.055f + i * 0.91f) * pixel * 0.65f;
            float x = Mathf.Lerp(r.xMin + pixel * 3f, r.xMax - pixel * 3f, seedX) + drift;
            float y = Mathf.Lerp(minY, maxY, seedY) + rise;
            float size = pixel * (Hash01(i * 71 + 3) > 0.74f ? 2f : 1f);
            Quad(mesh, x, y, size, size, particleColor);
        }
    }

    /// <summary>水面污膜用断续短片表现，不做海水那种亮白泡沫。</summary>
    private void DrawSurfaceDebris(VertexHelper mesh, Rect r, float bottom, float surface, float wavePixels, float pixel)
    {
        if (style.SurfaceDebris <= 0.01f || surface <= bottom)
            return;

        Color debrisColor = Color.Lerp(style.Surface, style.Detail, 0.28f);
        debrisColor.a *= 0.9f;
        const int patches = 11;
        for (int i = 0; i < patches; i++)
        {
            if (Hash01(i * 97 + 41) > style.SurfaceDebris)
                continue;

            float normalizedX = (i + 0.35f + Hash01(i * 23 + 7) * 0.3f) / patches;
            int waveColumn = Mathf.Clamp(Mathf.FloorToInt(normalizedX * 32f), 0, 31);
            float top = surface + Mathf.Round(GetWave(waveColumn) * wavePixels) * pixel;
            float width = pixel * Mathf.Lerp(2f, 5f, Hash01(i * 31 + 13));
            Quad(mesh, Mathf.Lerp(r.xMin, r.xMax, normalizedX) - width * 0.5f,
                Mathf.Max(bottom, top - pixel * 0.55f), width, pixel, debrisColor);
        }
    }

    #region 黏稠液体表现

    /// <summary>缓慢移动的宽高光表现糖浆的厚度和光泽，不复用泥沙或海水泡沫。</summary>
    private void DrawViscousHighlights(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
    {
        if (style.Viscosity <= 0.01f || surface - bottom < pixel * 5f)
            return;

        Color highlight = style.Surface;
        highlight.a *= style.Viscosity * 0.55f;
        for (int i = 0; i < 3; i++)
        {
            float x = Mathf.Lerp(r.xMin, r.xMax, 0.25f + i * 0.23f) +
                      Mathf.Sin(frame * 0.025f + i * 2f) * pixel * 2f;
            float y = Mathf.Lerp(bottom, surface, 0.7f + i * 0.08f);
            float width = pixel * (7f - i);
            Quad(mesh, x, y, width, pixel, highlight);
            Quad(mesh, x + pixel, y - pixel, width - pixel * 2f, pixel, Color.Lerp(style.Body, highlight, 0.35f));
        }
    }

    /// <summary>黏稠液体使用更宽、更慢的波峰，并抑制高频晃动。</summary>
    private float GetWave(int column)
    {
        float speed = Mathf.Lerp(1f, 0.16f, style.Viscosity);
        return Mathf.Sin(column * Mathf.Lerp(0.6f, 0.23f, style.Viscosity) + frame * 0.3f * speed) +
               Mathf.Sin(column * 1.37f - frame * 0.48f * speed) * agitation *
               Mathf.Lerp(0.65f, 0.1f, style.Viscosity);
    }

    #endregion

    /// <summary>无需分配的确定性散列，用于固定颗粒和污膜位置。</summary>
    private static float Hash01(int value)
    {
        unchecked
        {
            uint x = (uint)value;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (x & 0x00ffffffu) / 16777215f;
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
