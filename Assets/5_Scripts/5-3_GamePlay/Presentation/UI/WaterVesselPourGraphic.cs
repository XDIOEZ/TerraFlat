using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 陶罐倾倒时的像素液流表现。只负责 UI 可视化：起点跟随罐口旋转，液流在重力作用下向下弯曲，
/// 并以分段水片的方式逐渐衰减；真实液体扣减仍由 <see cref="Mod_WaterVessel"/> 结算。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaterVesselPourGraphic : MaskableGraphic
{
    private const float VisibleThreshold = 0.01f;
    private const int StreamSegments = 18; // 原 9 段翻倍，减小折线感并保持曲线连续。
    private const float JointOverlapRatio = 0.08f; // 相邻水片在接头处轻微重叠，消除像素栅格造成的黑缝。

    private Vector2 outletPosition; // 由正式 Prefab 的罐口锚点提供，不再从罐体中心猜测出水位置。
    private Vector2 outletDirection = Vector2.up; // 罐口朝外方向，随罐体旋转同步更新。
    private float flow;
    private float targetFlow;
    private float pulseUntil;
    private float nextFrame;
    private int frame;
    private Color bodyColor = Color.white;
    private Color surfaceColor = Color.white;
    private Color detailColor = Color.white;
    private float murkiness;

    /// <summary>有真实液体流失时触发一次液流脉冲；多次连续触发会自然叠加为持续倾倒。</summary>
    public void Emit(float normalizedFlow, Color body, Color surface, Color detail, float liquidMurkiness)
    {
        bodyColor = body;
        surfaceColor = surface;
        detailColor = detail;
        murkiness = Mathf.Clamp01(liquidMurkiness);

        float strength = Mathf.Clamp01(normalizedFlow);
        targetFlow = Mathf.Max(targetFlow, strength);
        flow = Mathf.Max(flow, Mathf.Lerp(0.22f, 0.92f, strength));
        pulseUntil = Mathf.Max(pulseUntil, Time.unscaledTime + Mathf.Lerp(0.2f, 0.5f, strength));
        SetVerticesDirty();
    }

    /// <summary>同步真实罐口锚点与朝向；液流始终从 Prefab 指定的嘴沿位置生成。</summary>
    public void SetOutletPose(Vector2 position, Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        bool unchanged = (outletPosition - position).sqrMagnitude <= 0.0001f &&
                         Vector2.Dot(outletDirection, direction) >= 0.9999f;
        outletPosition = position;
        outletDirection = direction;
        if (unchanged)
            return;

        if (flow > VisibleThreshold)
            SetVerticesDirty();
    }

    /// <summary>关闭/切换容器时立即清空；普通松手则让已有液流自行衰减。</summary>
    public void Clear(bool immediate)
    {
        targetFlow = 0f;
        pulseUntil = 0f;
        if (!immediate)
            return;

        flow = 0f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (Time.unscaledTime >= pulseUntil)
            targetFlow = 0f;

        float speed = targetFlow > flow ? 5.5f : 2.8f;
        float previous = flow;
        flow = Mathf.MoveTowards(flow, targetFlow, speed * Time.unscaledDeltaTime);

        if (flow <= VisibleThreshold && targetFlow <= VisibleThreshold)
        {
            if (previous > VisibleThreshold)
                SetVerticesDirty();
            flow = 0f;
            return;
        }

        if (Time.unscaledTime < nextFrame && Mathf.Approximately(previous, flow))
            return;

        nextFrame = Time.unscaledTime + 1f / 18f;
        frame++;
        SetVerticesDirty();
    }

    /// <summary>从旋转后的罐口沿初始喷出方向延伸，再受重力向下弯曲，形成分段液片。</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (flow <= VisibleThreshold)
            return;

        Rect rect = rectTransform.rect;
        float size = Mathf.Min(rect.width, rect.height);
        Vector2 mouth = outletPosition;
        Vector2 outward = outletDirection;
        float launchDistance = Mathf.Lerp(size * 0.1f, size * 0.26f, flow);
        float fallDistance = Mathf.Lerp(size * 0.32f, size * 0.68f, flow);
        float baseWidth = Mathf.Lerp(size * 0.025f, size * 0.085f, flow) * Mathf.Lerp(1f, 1.28f, murkiness);

        // 根部退进罐口，让后绘制的陶罐本体自然遮住内部水段，只显示真正越过嘴沿的部分。
        Vector2 previous = mouth - outward * (size * 0.035f);
        for (int i = 1; i <= StreamSegments; i++)
        {
            float t = i / (float)StreamSegments;
            // 段数翻倍后仍按整条液流的归一化位置计算波相位，避免因为细分增加而把波纹频率也翻倍。
            float wave = Mathf.Sin(frame * 0.55f + t * 13.05f) * size * 0.012f * flow * Mathf.Lerp(1f, 0.72f, murkiness);
            Vector2 current = mouth +
                              outward * (launchDistance * t) +
                              Vector2.down * (fallDistance * t * t) +
                              Vector2.right * wave;

            float width = baseWidth * Mathf.Lerp(1f, 0.58f, t);
            Vector2 segmentDirection = (current - previous).normalized;
            Vector2 start = previous - segmentDirection * width * JointOverlapRatio;
            Color ribbonColor = i % (murkiness > 0.35f ? 10 : 6) == 0
                ? Color.Lerp(bodyColor, surfaceColor, Mathf.Lerp(1f, 0.38f, murkiness))
                : bodyColor;
            AddRibbon(mesh, start, current, width, ribbonColor);

            if (murkiness > 0.05f && i % 4 == 0)
            {
                float phase = frame * 0.31f + i * 1.91f;
                Vector2 fleckCenter = Vector2.Lerp(start, current, 0.58f) +
                                      new Vector2(Mathf.Sin(phase), Mathf.Cos(phase * 0.73f)) * width * 0.17f;
                float fleckSize = size * Mathf.Lerp(0.008f, 0.018f, murkiness) * (0.75f + 0.25f * Mathf.Sin(phase));
                Color fleckColor = detailColor;
                fleckColor.a *= 0.82f;
                AddSquare(mesh, fleckCenter, fleckSize, fleckColor);
            }
            previous = current;
        }
    }

    /// <summary>添加一段沿路径方向旋转的无贴图水片。</summary>
    private static void AddRibbon(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude <= 0.0001f || width <= 0f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(from - normal, color, Vector2.zero);
        mesh.AddVert(from + normal, color, Vector2.zero);
        mesh.AddVert(to + normal, color, Vector2.zero);
        mesh.AddVert(to - normal, color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>给浑浊液流添加少量泥沙颗粒，保持纯程序化且不依赖额外贴图。</summary>
    private static void AddSquare(VertexHelper mesh, Vector2 center, float size, Color color)
    {
        if (size <= 0f)
            return;

        float half = size * 0.5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(center + new Vector2(-half, -half), color, Vector2.zero);
        mesh.AddVert(center + new Vector2(-half, half), color, Vector2.zero);
        mesh.AddVert(center + new Vector2(half, half), color, Vector2.zero);
        mesh.AddVert(center + new Vector2(half, -half), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
