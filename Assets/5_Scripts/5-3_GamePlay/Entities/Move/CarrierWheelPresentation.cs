using UnityEngine;

/// <summary>侧视车架保持直立，车轮和独立曲柄只随真实位移转动，座位随翻面镜像。</summary>
[DefaultExecutionOrder(-100)]
public sealed class CarrierWheelPresentation : MonoBehaviour, ICarrierSeatOffsetResolver, IShadowFootprintBoundsProvider
{
    #region 车轮表现配置与状态
    public SpriteRenderer FrameRenderer;
    public Sprite FrameSprite;
    public Transform RearWheel;
    public Transform FrontWheel;
    public Transform PedalCrank;
    [Min(.01f)] public float WheelRadius = .28f;
    [Min(0f)] public float PedalTurnsPerWheelTurn = .55f;
    public float WheelAngle { get; private set; }
    public float PedalAngle { get; private set; }
    private Vector2 previousPosition;
    private Vector3 rearOffset, frontOffset, crankOffset;
    private SpriteRenderer rearWheelSprite, frontWheelSprite;
    private bool offsetsCaptured;
    private bool facesLeft;
    #endregion

    #region 位移驱动与生命周期
    private void OnEnable() => ResetWheelPresentationMotionBaseline();

    public void ResetWheelPresentationMotionBaseline()
    {
        previousPosition = transform.position;
        rearWheelSprite = RearWheel != null ? RearWheel.GetComponent<SpriteRenderer>() : null;
        frontWheelSprite = FrontWheel != null ? FrontWheel.GetComponent<SpriteRenderer>() : null;
        if (!offsetsCaptured && RearWheel != null && FrontWheel != null)
        {
            rearOffset = RearWheel.localPosition;
            frontOffset = FrontWheel.localPosition;
            if (PedalCrank != null) crankOffset = PedalCrank.localPosition;
            offsetsCaptured = true;
        }
        WheelAngle = PedalAngle = 0f;
        facesLeft = false;
        ApplyWheelPose();
    }

    private void LateUpdate()
    {
        if (FrameRenderer != null && FrameSprite != null && FrameRenderer.sprite != FrameSprite)
            FrameRenderer.sprite = FrameSprite;
        Vector2 position = transform.position;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(previousPosition, position);
        previousPosition = position;
        // 传送和重新投影只重设基线，不能把位置突变当作骑行距离。
        if (displacement.sqrMagnitude > 4f) return;
        AdvanceWheelRotationByDisplacement(displacement);
    }

    public void AdvanceWheelRotationByDisplacement(Vector2 displacement)
    {
        if (!offsetsCaptured || WheelRadius <= 0f || displacement.sqrMagnitude <= .00000001f) return;
        if (Mathf.Abs(displacement.x) > .0001f) facesLeft = displacement.x < 0f;
        float wheelDelta = CalculateWheelRotationDegrees(displacement.magnitude, WheelRadius, facesLeft);
        WheelAngle = Mathf.Repeat(WheelAngle + wheelDelta, 360f);
        PedalAngle = Mathf.Repeat(PedalAngle + wheelDelta * PedalTurnsPerWheelTurn, 360f);
        ApplyWheelPose();
    }

    // 坐垫在贴图里随 FlipX 翻转，玩家脚底的世界锚点也要按相同方向镜像。
    public Vector2 ResolveSeatOffset(Vector2 configuredOffset)
        => facesLeft ? new Vector2(-configuredOffset.x, configuredOffset.y) : configuredOffset;

    public static float CalculateWheelRotationDegrees(float distance, float radius, bool left)
        => radius > 0f ? distance / radius * Mathf.Rad2Deg * (left ? 1f : -1f) : 0f;

    /// <summary>车架与两轮合并计算落地轮廓，避免阴影被夹到车架图片底部。</summary>
    public Bounds MeasureWorldShadowFootprintBounds(SpriteRenderer sourceRenderer)
    {
        Bounds footprint = ShadowFootprintResolver.MeasureVisibleWorldBounds(sourceRenderer);
        IncludeWheelShadowBounds(ref footprint, rearWheelSprite);
        IncludeWheelShadowBounds(ref footprint, frontWheelSprite);
        return footprint;
    }

    private static void IncludeWheelShadowBounds(ref Bounds footprint, SpriteRenderer wheel)
    {
        if (wheel != null && wheel.enabled && wheel.gameObject.activeInHierarchy && wheel.sprite != null)
            footprint.Encapsulate(ShadowFootprintResolver.MeasureVisibleWorldBounds(wheel));
    }

    private void ApplyWheelPose()
    {
        if (!offsetsCaptured || FrameRenderer == null) return;
        FrameRenderer.flipX = facesLeft;
        float mirror = facesLeft ? -1f : 1f;
        RearWheel.localPosition = new Vector3(rearOffset.x * mirror, rearOffset.y, rearOffset.z);
        FrontWheel.localPosition = new Vector3(frontOffset.x * mirror, frontOffset.y, frontOffset.z);
        Quaternion rotation = Quaternion.Euler(0f, 0f, WheelAngle);
        RearWheel.localRotation = FrontWheel.localRotation = rotation;
        if (PedalCrank != null)
        {
            PedalCrank.localPosition = new Vector3(crankOffset.x * mirror, crankOffset.y, crankOffset.z);
            PedalCrank.localRotation = Quaternion.Euler(0f, 0f, PedalAngle);
        }
    }
    #endregion
}
