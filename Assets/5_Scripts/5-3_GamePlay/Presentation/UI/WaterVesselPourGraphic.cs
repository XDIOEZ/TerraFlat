using UnityEngine;
using UnityEngine.UI;

/// <summary>容器倾倒时的连续液流网格；表现按帧持续，玩法液量仍只由 Mod_WaterVessel 按整份结算。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaterVesselPourGraphic : MaskableGraphic
{
    private const float VisibleThreshold = 0.01f;
    private const int StreamRows = 30; // 纵向采样足够平滑，同时保持 UI 网格很轻。
    private const int CrossSectionColumns = 5; // 透明边缘、主体和中央高光组成有体积感的液流截面。
    private const float FlowHoldMinSeconds = 0.22f;
    private const float FlowHoldMaxSeconds = 0.48f;
    private const float FlowFadeOutPerSecond = 2.8f;
    private static readonly float[] CrossSectionOffsets = { -0.5f, -0.36f, 0f, 0.36f, 0.5f };

    private Vector2 outletPosition;
    private Vector2 outletDirection = Vector2.up;
    private Vector2 outletVelocity;
    private bool hasOutletPose;
    private float flow;
    private float targetFlow;
    private float pulseUntil;
    private float phase;
    private Color bodyColor = Color.white;
    private Color surfaceColor = Color.white;
    private Color detailColor = Color.white;
    private float murkiness;
    private float viscosity;

    #region 液流驱动

    /// <summary>倾倒期间按帧维持液流，离散的一份结算不会让水柱出现断帧。</summary>
    public void Emit(float normalizedFlow, Color body, Color surface, Color detail, float liquidMurkiness, float liquidViscosity = 0f)
    {
        bodyColor = body;
        surfaceColor = surface;
        detailColor = detail;
        murkiness = Mathf.Clamp01(liquidMurkiness);
        viscosity = Mathf.Clamp01(liquidViscosity);

        float strength = Mathf.Clamp01(normalizedFlow);
        targetFlow = Mathf.Max(targetFlow, strength);
        flow = Mathf.Max(flow, Mathf.Lerp(0.2f, 0.94f, strength));
        pulseUntil = Mathf.Max(
            pulseUntil,
            Time.unscaledTime + Mathf.Lerp(FlowHoldMinSeconds, FlowHoldMaxSeconds, strength) * Mathf.Lerp(1f, 1.8f, viscosity));
        SetVerticesDirty();
    }

    /// <summary>同步 Prefab 罐口真实位置、朝向和移动速度，让水柱继承容器运动惯性。</summary>
    public void SetOutletPose(Vector2 position, Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        if (hasOutletPose)
        {
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 1f / 120f);
            Vector2 measuredVelocity = (position - outletPosition) / deltaTime;
            outletVelocity = Vector2.Lerp(outletVelocity, measuredVelocity, 0.34f);
        }
        else
        {
            outletVelocity = Vector2.zero;
            hasOutletPose = true;
        }

        bool unchanged = (outletPosition - position).sqrMagnitude <= 0.0001f &&
                         Vector2.Dot(outletDirection, direction) >= 0.9999f;
        outletPosition = position;
        outletDirection = direction;
        if (!unchanged && flow > VisibleThreshold)
            SetVerticesDirty();
    }

    /// <summary>切换目标时可立即清空；普通松手则让当前水柱自然收束。</summary>
    public void Clear(bool immediate)
    {
        targetFlow = 0f;
        pulseUntil = 0f;
        if (!immediate)
            return;

        flow = 0f;
        outletVelocity = Vector2.zero;
        SetVerticesDirty();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        if (Time.unscaledTime >= pulseUntil)
            targetFlow = 0f;

        float speed = (targetFlow > flow ? 6.8f : FlowFadeOutPerSecond) * Mathf.Lerp(1f, 0.42f, viscosity);
        float previous = flow;
        flow = Mathf.MoveTowards(flow, targetFlow, speed * deltaTime);
        outletVelocity = Vector2.Lerp(outletVelocity, Vector2.zero, 1f - Mathf.Exp(-deltaTime * 7f));
        phase += deltaTime * Mathf.Lerp(13f, 3.5f, viscosity);

        if (flow <= VisibleThreshold && targetFlow <= VisibleThreshold)
        {
            if (previous > VisibleThreshold)
                SetVerticesDirty();
            flow = 0f;
            return;
        }

        // 连续网格按帧更新，避免旧的 18Hz 分段水片抖动。
        SetVerticesDirty();
    }

    #endregion

    #region 连续液流网格

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (flow <= VisibleThreshold || !hasOutletPose)
            return;

        Rect rect = rectTransform.rect;
        float size = Mathf.Max(1f, Mathf.Min(rect.width, rect.height));
        Vector2 outward = outletDirection.normalized;
        float baseWidth = Mathf.Lerp(size * 0.026f, size * 0.082f, flow) *
                          Mathf.Lerp(1f, 1.2f, viscosity) *
                          Mathf.Lerp(1f, 1.12f, murkiness);

        DrawMouthBridge(mesh, size, outward, baseWidth);
        DrawBallisticStream(mesh, size, outward, baseWidth);
    }

    /// <summary>用同一截面网格跨过厚罐口，保证罐内液体和外部水柱视觉连续。</summary>
    private void DrawMouthBridge(VertexHelper mesh, float size, Vector2 outward, float baseWidth)
    {
        Vector2 normal = new Vector2(-outward.y, outward.x);
        float bridgeLength = size * 0.085f;
        Vector2 from = outletPosition - outward * bridgeLength;
        Vector2 to = outletPosition + outward * (baseWidth * 0.16f);
        int previousRow = -1;

        for (int i = 0; i < 4; i++)
        {
            float t = i / 3f;
            Vector2 center = Vector2.Lerp(from, to, t);
            float width = baseWidth * Mathf.Lerp(0.54f, 1f, t);
            int row = AddCrossSection(mesh, center, normal, width, Mathf.Lerp(0.74f, 1f, t));
            if (previousRow >= 0)
                ConnectRows(mesh, previousRow, row);
            previousRow = row;
        }
    }

    /// <summary>沿受重力加速的弹道生成连续条带，截面根据速度自动收细，模拟真实液柱拉伸。</summary>
    private void DrawBallisticStream(VertexHelper mesh, float size, Vector2 outward, float baseWidth)
    {
        float launchSpeed = Mathf.Lerp(size * 0.82f, size * 1.78f, flow) * Mathf.Lerp(1f, 0.58f, viscosity);
        Vector2 inheritedVelocity = Vector2.ClampMagnitude(outletVelocity, size * 1.2f) * Mathf.Lerp(0.22f, 0.08f, viscosity);
        Vector2 initialVelocity = outward * launchSpeed + inheritedVelocity;
        float initialSpeed = Mathf.Max(size * 0.2f, initialVelocity.magnitude);
        Vector2 gravity = Vector2.down * (size * Mathf.Lerp(4.7f, 5.8f, flow));
        float travelTime = Mathf.Lerp(0.34f, 0.52f, flow) * Mathf.Lerp(1f, 0.82f, viscosity);
        float turbulence = size * 0.012f * flow * Mathf.Lerp(1f, 0.08f, viscosity);
        int previousRow = -1;

        for (int i = 0; i < StreamRows; i++)
        {
            float t = i / (float)(StreamRows - 1);
            float time = travelTime * t;
            Vector2 velocity = initialVelocity + gravity * time;
            Vector2 direction = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : outward;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 center = outletPosition + initialVelocity * time + gravity * (0.5f * time * time);

            float wave =
                Mathf.Sin(phase + t * 11.2f) * 0.68f +
                Mathf.Sin(phase * 0.63f + t * 19.7f + 1.4f) * 0.32f;
            center += normal * (wave * turbulence * Mathf.SmoothStep(0f, 1f, t));

            // 二维液柱按速度增大而收细；末端少量颈缩让高速水流更自然。
            float speedRatio = initialSpeed / Mathf.Max(initialSpeed, velocity.magnitude);
            float conservationScale = Mathf.Sqrt(Mathf.Clamp(speedRatio, 0.18f, 1.15f));
            float neckingProgress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
            float necking = Mathf.Lerp(1f, 0.76f + 0.08f * Mathf.Sin(phase * 1.37f + t * 24f),
                neckingProgress * (1f - viscosity));
            float width = baseWidth * conservationScale * necking;
            float alpha = Mathf.Lerp(1f, 0.78f, t);

            int row = AddCrossSection(mesh, center, normal, width, alpha);
            if (previousRow >= 0)
                ConnectRows(mesh, previousRow, row);
            previousRow = row;
        }
    }

    /// <summary>每一排使用透明边缘 + 主体 + 高光，插值后形成柔和的液体横截面。</summary>
    private int AddCrossSection(VertexHelper mesh, Vector2 center, Vector2 normal, float width, float alpha)
    {
        int start = mesh.currentVertCount;
        Color edge = MultiplyAlpha(bodyColor, 0f);
        Color body = MultiplyAlpha(bodyColor, alpha);
        Color highlightBase = Color.Lerp(surfaceColor, detailColor, murkiness * 0.22f);
        Color highlight = MultiplyAlpha(
            Color.Lerp(bodyColor, highlightBase, Mathf.Lerp(0.72f, 0.3f, murkiness)),
            alpha * Mathf.Lerp(0.9f, 0.58f, murkiness));

        for (int i = 0; i < CrossSectionColumns; i++)
        {
            float offset = CrossSectionOffsets[i] * width;
            Color vertexColor = i == 0 || i == CrossSectionColumns - 1
                ? edge
                : i == CrossSectionColumns / 2 ? highlight : body;
            mesh.AddVert(center + normal * offset, vertexColor, Vector2.zero);
        }
        return start;
    }

    private static void ConnectRows(VertexHelper mesh, int previousRow, int currentRow)
    {
        for (int i = 0; i < CrossSectionColumns - 1; i++)
        {
            int a = previousRow + i;
            int b = previousRow + i + 1;
            int c = currentRow + i + 1;
            int d = currentRow + i;
            mesh.AddTriangle(a, b, c);
            mesh.AddTriangle(a, c, d);
        }
    }

    private static Color MultiplyAlpha(Color color, float multiplier)
    {
        color.a *= Mathf.Clamp01(multiplier);
        return color;
    }

    #endregion
}
