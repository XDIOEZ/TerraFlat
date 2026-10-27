using UnityEngine;

/// <summary>侧视车架保持直立，车轮只按车体真实位移转动，与骑行和推动来源无关。</summary>
public sealed class CarrierWheelPresentation : MonoBehaviour
{
    #region 车轮表现配置与状态
    public SpriteRenderer FrameRenderer;
    public Sprite FrameSprite;
    public Transform RearWheel;
    public Transform FrontWheel;
    [Min(.01f)] public float WheelRadius = .28f;
    public float WheelAngle { get; private set; }
    private Vector2 previousPosition;
    private Vector3 rearOffset, frontOffset;
    private bool offsetsCaptured;
    private bool facesLeft;
    #endregion

    #region 位移驱动与生命周期
    private void OnEnable() => ResetWheelPresentationMotionBaseline();

    public void ResetWheelPresentationMotionBaseline()
    {
        previousPosition = transform.position;
        if (!offsetsCaptured && RearWheel != null && FrontWheel != null)
        {
            rearOffset = RearWheel.localPosition;
            frontOffset = FrontWheel.localPosition;
            offsetsCaptured = true;
        }
        WheelAngle = 0f;
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
        WheelAngle = Mathf.Repeat(WheelAngle + CalculateWheelRotationDegrees(displacement.magnitude,
            WheelRadius, facesLeft), 360f);
        ApplyWheelPose();
    }

    public static float CalculateWheelRotationDegrees(float distance, float radius, bool left)
        => radius > 0f ? distance / radius * Mathf.Rad2Deg * (left ? 1f : -1f) : 0f;

    private void ApplyWheelPose()
    {
        if (!offsetsCaptured || FrameRenderer == null) return;
        FrameRenderer.flipX = facesLeft;
        float mirror = facesLeft ? -1f : 1f;
        RearWheel.localPosition = new Vector3(rearOffset.x * mirror, rearOffset.y, rearOffset.z);
        FrontWheel.localPosition = new Vector3(frontOffset.x * mirror, frontOffset.y, frontOffset.z);
        Quaternion rotation = Quaternion.Euler(0f, 0f, WheelAngle);
        RearWheel.localRotation = FrontWheel.localRotation = rotation;
    }
    #endregion
}
