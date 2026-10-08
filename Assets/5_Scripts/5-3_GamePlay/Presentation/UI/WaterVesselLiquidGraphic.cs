using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>容器剖面的连续液体网格：液面使用一维弹簧/浅水近似传播波动，外部 Mask 负责裁切容器轮廓。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WaterVesselLiquidGraphic : MaskableGraphic
{
    private const int SurfaceSampleCount = 49; // 49 个液面质点足以消除台阶感，同时保持 UI 模拟开销极低。
    private const float PhysicsStep = 1f / 60f; // 固定步长保证不同帧率下波传播一致。
    private const int MaxPhysicsStepsPerFrame = 4; // 卡顿帧不无限补算，避免 UI 模拟拖垮主线程。
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
        [Range(0f, 1f)] public float Viscosity; // 运行时由 LiquidDefinition.viscosity 覆盖；Prefab 值仅保留序列化兼容。
        public bool Foam; // 海水泡沫。
    }
    public LiquidStyle[] Styles; // Prefab 配置视觉，不修改液体玩法定义。
    public Vector2 FillRange = new Vector2(20f / 128f, 94f / 128f); // 罐内可用水位的归一化高度。
    private LiquidStyle style;
    private string liquidId;
    private float level, targetLevel, nextDecorationFrame;
    private float agitation; // 来回摇晃产生的额外水面波动，随时间自然衰减。
    private float vesselTiltDegrees; // 内腔遮罩的倾角，用于扩展水平液层的绘制范围。
    private int frame;
    private readonly float[] surfaceDisplacement = new float[SurfaceSampleCount]; // 相对静止液面的高度偏移。
    private readonly float[] surfaceVelocity = new float[SurfaceSampleCount]; // 每个质点的竖直速度。
    private float physicsAccumulator;
    private float lastTiltAngularVelocity;
    private bool surfaceAwake;

    public Color CurrentBodyColor => style.Body;
    public Color CurrentSurfaceColor => style.Surface;
    public Color CurrentDetailColor => style.Detail;
    public float CurrentMurkiness => style.Murkiness;
    public float CurrentViscosity => style.Viscosity;

    /// <summary>液层反向旋转时，按容器倾角扩展网格，避免露出矩形边界。</summary>
    public void SetVesselTilt(float tiltDegrees)
    {
        if (Mathf.Approximately(vesselTiltDegrees, tiltDegrees)) return;
        float deltaTime = Mathf.Max(Time.unscaledDeltaTime, PhysicsStep);
        float angularVelocity = Mathf.DeltaAngle(vesselTiltDegrees, tiltDegrees) / deltaTime;
        vesselTiltDegrees = tiltDegrees;
        InjectTiltImpulse(angularVelocity);
        SetVerticesDirty();
    }

    /// <summary>接收容器真实数据，打开时直接定位，使用过程中平滑升降。</summary>
    public void SetWater(float amount, int capacity, LiquidDefinition liquid, bool immediate = false)
    {
        float value = capacity > 0 ? Mathf.Clamp01(amount / capacity) : 0f;
        if (amount > 0f && liquid == null)
            throw new InvalidOperationException("非空液体容器缺少 LiquidDefinition。");

        float viscosity = liquid?.VisualViscosity01 ?? 0f;
        bool changedStyle = amount > 0f &&
            (!string.Equals(liquid?.Id, liquidId, StringComparison.OrdinalIgnoreCase) ||
             !Mathf.Approximately(style.Viscosity, viscosity) ||
             liquid?.Category == "mixture" && style.Body != liquid.PrimaryColor);
        if (changedStyle)
        {
            style = ResolveStyle(liquid);
            liquidId = liquid.Id;
            agitation = 0f;
            ResetSurfaceSimulation();
        }
        if (!immediate && !changedStyle && targetLevel == value) return;
        targetLevel = value;
        if (immediate)
        {
            level = value;
            ResetSurfaceSimulation();
        }
        SetVerticesDirty();
    }

    /// <summary>特殊液体可继续复用 Prefab 样式；未配置状态时按液体主色自动生成，避免每种熔融液体都复制一份 UI 配置。</summary>
    private LiquidStyle ResolveStyle(LiquidDefinition liquid)
    {
        int index = Array.FindIndex(Styles,
            entry => string.Equals(entry.VisualState, liquid.VisualState, StringComparison.OrdinalIgnoreCase));
        LiquidStyle resolved;
        if (index >= 0 && liquid.Category != "mixture")
        {
            resolved = Styles[index];
        }
        else
        {
            Color body = liquid.PrimaryColor;
            resolved = new LiquidStyle
            {
                VisualState = liquid.VisualState,
                Body = body,
                Surface = Color.Lerp(body, Color.white, 0.38f),
                Detail = Color.Lerp(body, Color.white, 0.18f),
                Deep = Color.Lerp(body, Color.black, 0.35f),
                Murkiness = 0f,
                Sediment = 0f,
                SurfaceDebris = 0f,
                SuspendedParticles = 0f,
                Foam = false
            };
        }

        resolved.Viscosity = liquid.VisualViscosity01;
        return resolved;
    }

    /// <summary>把额外扰动直接注入液面网格；倒液和快速摇晃都只驱动表现，不参与液量结算。</summary>
    public void AddAgitation(float normalizedImpulse)
    {
        if (level <= 0f && targetLevel <= 0f)
            return;

        float impulse = Mathf.Clamp01(normalizedImpulse) * Mathf.Lerp(1f, 0.28f, style.Viscosity);
        agitation = Mathf.Clamp01(Mathf.Max(agitation, impulse));
        if (impulse <= 0.001f)
            return;

        float direction = Mathf.Abs(lastTiltAngularVelocity) > 2f
            ? -Mathf.Sign(lastTiltAngularVelocity)
            : (frame & 1) == 0 ? 1f : -1f;
        float velocityKick = rectTransform.rect.height * 0.095f * impulse;
        for (int i = 0; i < SurfaceSampleCount; i++)
        {
            float x = i / (float)(SurfaceSampleCount - 1) * 2f - 1f;
            float edgeWeight = Mathf.Lerp(0.3f, 1f, Mathf.Abs(x));
            surfaceVelocity[i] += direction * x * edgeWeight * velocityKick;
        }
        surfaceAwake = true;
    }

    /// <summary>固定步长推进连续液面；静止后自动休眠，只保留低频装饰刷新。</summary>
    private void Update()
    {
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, PhysicsStep * MaxPhysicsStepsPerFrame);
        if (deltaTime <= 0f)
            return;

        agitation = Mathf.MoveTowards(agitation, 0f, deltaTime * Mathf.Lerp(1.6f, 3.2f, style.Viscosity));
        float previousLevel = level;
        level = Mathf.MoveTowards(level, targetLevel,
            deltaTime * Mathf.Lerp(1.05f, 0.46f, style.Viscosity));

        bool simulated = false;
        if (level > 0f || targetLevel > 0f)
        {
            physicsAccumulator += deltaTime;
            int steps = 0;
            while (physicsAccumulator >= PhysicsStep && steps++ < MaxPhysicsStepsPerFrame)
            {
                simulated |= StepSurfacePhysics(PhysicsStep);
                physicsAccumulator -= PhysicsStep;
            }
            if (steps >= MaxPhysicsStepsPerFrame)
                physicsAccumulator = 0f;
        }
        else
        {
            physicsAccumulator = 0f;
            ResetSurfaceSimulation();
        }

        bool decorationTick = Time.unscaledTime >= nextDecorationFrame;
        if (decorationTick)
        {
            nextDecorationFrame = Time.unscaledTime + 1f / 18f;
            frame++;
        }

        if (simulated || decorationTick || !Mathf.Approximately(previousLevel, level))
            SetVerticesDirty();
    }

    #region 连续液面物理

    /// <summary>罐体角加速度产生横向惯性；反向摇摆会在两侧形成更强的传播波。</summary>
    private void InjectTiltImpulse(float angularVelocity)
    {
        if (level <= 0f && targetLevel <= 0f)
            return;

        float currentAngularVelocity = Mathf.Clamp(angularVelocity, -720f, 720f);
        float angularAcceleration = (currentAngularVelocity - lastTiltAngularVelocity) /
                                    Mathf.Max(Time.unscaledDeltaTime, PhysicsStep);
        bool reversed = Mathf.Abs(lastTiltAngularVelocity) > 12f &&
                        Mathf.Abs(currentAngularVelocity) > 12f &&
                        Mathf.Sign(lastTiltAngularVelocity) != Mathf.Sign(currentAngularVelocity);
        lastTiltAngularVelocity = currentAngularVelocity;
        float normalized = Mathf.Clamp(angularAcceleration / 8500f, -1f, 1f);
        float viscosityResponse = Mathf.Lerp(1f, 0.18f, style.Viscosity);
        float reversalBoost = reversed ? 1.45f : 1f;
        float impulse = rectTransform.rect.height * 0.62f * normalized * viscosityResponse * reversalBoost;
        for (int i = 0; i < SurfaceSampleCount; i++)
        {
            float x = i / (float)(SurfaceSampleCount - 1) * 2f - 1f;
            float edgeWeight = Mathf.Lerp(0.38f, 1f, Mathf.Abs(x));
            surfaceVelocity[i] += -x * edgeWeight * impulse;
        }
        agitation = Mathf.Max(agitation, Mathf.Clamp01(Mathf.Abs(normalized) * reversalBoost));
        surfaceAwake = true;
    }

    /// <summary>弹簧回复 + 相邻质点拉普拉斯耦合近似浅水波；每步移除平均位移以保持液体体积。</summary>
    private bool StepSurfacePhysics(float deltaTime)
    {
        if (!surfaceAwake)
            return false;

        float viscosity = style.Viscosity;
        float spring = Mathf.Lerp(27f, 13f, viscosity);
        float coupling = Mathf.Lerp(215f, 42f, viscosity);
        float damping = Mathf.Lerp(2.5f, 12.5f, viscosity);
        float maximumAmplitude = rectTransform.rect.height * Mathf.Lerp(0.115f, 0.05f, viscosity) *
                                 Mathf.Min(1f, Mathf.Max(level, targetLevel) * 4f);
        float maximumVelocity = rectTransform.rect.height * Mathf.Lerp(2.25f, 0.8f, viscosity);
        float maxVelocity = 0f;
        float maxDisplacement = 0f;

        for (int i = 0; i < SurfaceSampleCount; i++)
        {
            float current = surfaceDisplacement[i];
            float left = surfaceDisplacement[i > 0 ? i - 1 : i];
            float right = surfaceDisplacement[i + 1 < SurfaceSampleCount ? i + 1 : i];
            float laplacian = left + right - current * 2f;
            float acceleration = -spring * current + coupling * laplacian - damping * surfaceVelocity[i];
            surfaceVelocity[i] = Mathf.Clamp(
                surfaceVelocity[i] + acceleration * deltaTime,
                -maximumVelocity,
                maximumVelocity);
        }

        float mean = 0f;
        for (int i = 0; i < SurfaceSampleCount; i++)
        {
            surfaceDisplacement[i] = Mathf.Clamp(
                surfaceDisplacement[i] + surfaceVelocity[i] * deltaTime,
                -maximumAmplitude,
                maximumAmplitude);
            mean += surfaceDisplacement[i];
        }
        mean /= SurfaceSampleCount;

        for (int i = 0; i < SurfaceSampleCount; i++)
        {
            surfaceDisplacement[i] -= mean;
            maxVelocity = Mathf.Max(maxVelocity, Mathf.Abs(surfaceVelocity[i]));
            maxDisplacement = Mathf.Max(maxDisplacement, Mathf.Abs(surfaceDisplacement[i]));
        }

        float restScale = Mathf.Max(1f, rectTransform.rect.height);
        if (maxVelocity < restScale * 0.003f && maxDisplacement < restScale * 0.0005f)
        {
            ResetSurfaceSimulation();
            return true;
        }
        return true;
    }

    private void ResetSurfaceSimulation()
    {
        Array.Clear(surfaceDisplacement, 0, surfaceDisplacement.Length);
        Array.Clear(surfaceVelocity, 0, surfaceVelocity.Length);
        physicsAccumulator = 0f;
        lastTiltAngularVelocity = 0f;
        surfaceAwake = false;
    }

    private float SampleSurfaceDisplacement(float normalizedX)
    {
        float position = Mathf.Clamp01(normalizedX) * (SurfaceSampleCount - 1);
        int left = Mathf.FloorToInt(position);
        int right = Mathf.Min(left + 1, SurfaceSampleCount - 1);
        return Mathf.Lerp(surfaceDisplacement[left], surfaceDisplacement[right], position - left);
    }

    #endregion

    /// <summary>按连续液面网格绘制水体；相邻顶点直接连接，不再逐列取整，因此不会出现阶梯水面。</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (level <= 0) return;
        Rect vesselRect = rectTransform.rect;
        float radians = vesselTiltDegrees * Mathf.Deg2Rad;
        float cosine = Mathf.Abs(Mathf.Cos(radians));
        float sine = Mathf.Abs(Mathf.Sin(radians));
        float width = vesselRect.width * cosine + vesselRect.height * sine;
        float height = vesselRect.width * sine + vesselRect.height * cosine;
        Rect r = new Rect(vesselRect.center.x - width * 0.5f, vesselRect.center.y - height * 0.5f, width, height);
        float pixel = vesselRect.height / 128f;
        float fillBottom = vesselRect.yMin + vesselRect.height * FillRange.x;
        float bottom = Mathf.Min(fillBottom, r.yMin);
        float surface = Mathf.Lerp(fillBottom, vesselRect.yMin + vesselRect.height * FillRange.y, level);
        DrawContinuousBody(mesh, r, bottom, surface, pixel);

        DrawSediment(mesh, r, bottom, surface, pixel);
        DrawSuspendedParticles(mesh, r, bottom, surface, pixel);
        DrawSurfaceDebris(mesh, r, bottom, surface, pixel);
        DrawViscousHighlights(mesh, r, bottom, surface, pixel);
    }

    /// <summary>将相邻液面采样点组成连续梯形带；清水一层，浑浊/黏稠液体用多层渐变。</summary>
    private void DrawContinuousBody(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
    {
        Color deep = style.Deep.a > 0f ? style.Deep : style.Detail;
        int bands = style.Murkiness <= 0.01f && style.Viscosity <= 0.01f ? 1 : 5;
        float surfaceThickness = Mathf.Max(pixel * 0.9f, r.height * 0.006f);
        for (int i = 0; i < SurfaceSampleCount - 1; i++)
        {
            float t0 = i / (float)(SurfaceSampleCount - 1);
            float t1 = (i + 1) / (float)(SurfaceSampleCount - 1);
            float x0 = Mathf.Lerp(r.xMin, r.xMax, t0);
            float x1 = Mathf.Lerp(r.xMin, r.xMax, t1);
            float top0 = Mathf.Max(bottom, surface + surfaceDisplacement[i]);
            float top1 = Mathf.Max(bottom, surface + surfaceDisplacement[i + 1]);

            for (int band = 0; band < bands; band++)
            {
                float from = band / (float)bands;
                float to = (band + 1) / (float)bands;
                Color bandColor = bands == 1
                    ? style.Body
                    : Color.Lerp(deep, style.Body, Mathf.Pow((from + to) * 0.5f, 0.72f));
                AddTrapezoid(mesh,
                    x0, Mathf.Lerp(bottom, top0, from), Mathf.Lerp(bottom, top0, to),
                    x1, Mathf.Lerp(bottom, top1, from), Mathf.Lerp(bottom, top1, to),
                    bandColor);
            }

            AddTrapezoid(mesh,
                x0, Mathf.Max(bottom, top0 - surfaceThickness), top0,
                x1, Mathf.Max(bottom, top1 - surfaceThickness), top1,
                style.Surface);
        }

        DrawSurfaceAccents(mesh, r, bottom, surface, pixel);
    }

    /// <summary>泡沫和清水亮点贴着连续液面采样，装饰不会重新引入台阶。</summary>
    private void DrawSurfaceAccents(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
    {
        const int accents = 10;
        for (int i = 0; i < accents; i++)
        {
            float t = (i + 0.35f) / accents;
            float top = surface + SampleSurfaceDisplacement(t);
            if (top <= bottom)
                continue;
            if (style.Foam && i % 2 == 0)
                Quad(mesh, Mathf.Lerp(r.xMin, r.xMax, t), top - pixel, pixel * 3f, pixel, style.Detail);
            else if (style.Murkiness <= 0.01f && style.Viscosity <= 0.01f && i % 2 == 0)
            {
                Color glint = style.Detail;
                glint.a *= 0.6f;
                Quad(mesh, Mathf.Lerp(r.xMin, r.xMax, t), top - pixel * 0.55f, pixel * 1.5f, pixel * 0.55f, glint);
            }
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
    private void DrawSurfaceDebris(VertexHelper mesh, Rect r, float bottom, float surface, float pixel)
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
            float top = surface + SampleSurfaceDisplacement(normalizedX);
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

    /// <summary>添加左右高度可不同的连续四边形，液面与分层网格都复用这一条路径。</summary>
    private static void AddTrapezoid(
        VertexHelper mesh,
        float x0,
        float bottom0,
        float top0,
        float x1,
        float bottom1,
        float top1,
        Color color)
    {
        if (x1 <= x0 || top0 <= bottom0 && top1 <= bottom1)
            return;

        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(x0, bottom0, 0f), color, Vector2.zero);
        mesh.AddVert(new Vector3(x0, top0, 0f), color, Vector2.zero);
        mesh.AddVert(new Vector3(x1, top1, 0f), color, Vector2.zero);
        mesh.AddVert(new Vector3(x1, bottom1, 0f), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
