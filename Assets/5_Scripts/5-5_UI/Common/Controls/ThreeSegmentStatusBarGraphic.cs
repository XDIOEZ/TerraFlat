using UnityEngine;
using UnityEngine.UI;

/// <summary>用同一网格绘制连续的三段状态量，只负责显示，不修改业务数据。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ThreeSegmentStatusBarGraphic : MaskableGraphic
{
    #region 三段填充与过渡

    [SerializeField] private Color firstColor = new Color(0.44f, 0.30f, 0.08f, 1f);
    [SerializeField] private Color secondColor = new Color(0.12f, 0.32f, 0.48f, 1f);
    [SerializeField] private Color thirdColor = new Color(0.40f, 0.20f, 0.42f, 1f);
    private Vector3 startFractions;
    private Vector3 targetFractions;
    private float elapsed;
    private float duration;
    private bool initialized;

    public Vector3 SegmentFractions { get; private set; }
    public Vector3 TargetFractions => targetFractions;
    public Color FirstColor => firstColor;
    public Color SecondColor => secondColor;
    public Color ThirdColor => thirdColor;

    // 三段共用总容量和同一个过渡时钟，避免段间重叠、空隙或满条比例失真。
    public void SetSegmentValues(Vector3 values, float maximum, float transitionDuration)
    {
        values = Vector3.Max(Vector3.zero, values);
        float denominator = Mathf.Max(maximum, values.x + values.y + values.z);
        Vector3 next = maximum > 0f ? values / denominator : Vector3.zero;
        if (!initialized || transitionDuration <= 0f)
        {
            initialized = true;
            targetFractions = next;
            SegmentFractions = next;
            duration = elapsed = 0f;
            SetVerticesDirty();
            return;
        }
        if (targetFractions == next)
            return;

        startFractions = SegmentFractions;
        targetFractions = next;
        duration = transitionDuration;
        elapsed = 0f;
    }

    // 由所属 HUD 的既有协程推进，稳定状态不额外注册 Update 或重建网格。
    public void AdvanceTransition(float unscaledDeltaTime)
    {
        if (elapsed >= duration)
            return;
        elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, unscaledDeltaTime));
        float inverse = 1f - elapsed / duration;
        SegmentFractions = elapsed >= duration ? targetFractions :
            Vector3.LerpUnclamped(startFractions, targetFractions, 1f - inverse * inverse * inverse);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect bounds = GetPixelAdjustedRect();
        float cursor = bounds.xMin;
        AddSegment(vertices, bounds, ref cursor, SegmentFractions.x, firstColor);
        AddSegment(vertices, bounds, ref cursor, SegmentFractions.y, secondColor);
        AddSegment(vertices, bounds, ref cursor, SegmentFractions.z, thirdColor);
    }

    private void AddSegment(VertexHelper vertices, Rect bounds, ref float cursor, float fraction, Color segmentColor)
    {
        float end = Mathf.Min(bounds.xMax, cursor + bounds.width * fraction);
        if (end <= cursor)
            return;
        int firstVertex = vertices.currentVertCount;
        Color32 tint = segmentColor * color;
        vertices.AddVert(new Vector3(cursor, bounds.yMin), tint, Vector2.zero);
        vertices.AddVert(new Vector3(cursor, bounds.yMax), tint, Vector2.zero);
        vertices.AddVert(new Vector3(end, bounds.yMax), tint, Vector2.zero);
        vertices.AddVert(new Vector3(end, bounds.yMin), tint, Vector2.zero);
        vertices.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
        vertices.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        cursor = end;
    }

    #endregion
}
