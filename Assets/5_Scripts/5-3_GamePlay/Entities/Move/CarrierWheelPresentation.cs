using UnityEngine;

/// <summary>侧视车架按骑手光标左右翻面，车轮和独立曲柄只随真实位移转动，座位随翻面镜像。</summary>
[DefaultExecutionOrder(-100)]
public sealed class CarrierWheelPresentation : MonoBehaviour, ICarrierSeatOffsetResolver, IShadowFootprintBoundsProvider,
    IGameplayFixedStepSimulationSystem
{
    #region 车轮表现配置与状态
    public SpriteRenderer FrameRenderer;
    public Sprite FrameSprite;
    public Transform RearWheel;
    public Transform FrontWheel;
    public Transform PedalCrank;
    [Min(.01f)] public float WheelRadius = .28f;
    [Min(0f)] public float PedalTurnsPerWheelTurn = .55f;
    [Min(0f)] public float FacingDeadZonePixels = 48f;
    public float WheelAngle { get; private set; }
    public float PedalAngle { get; private set; }
    private Vector2 previousPosition;
    private Vector3 rearOffset, frontOffset, crankOffset;
    private SpriteRenderer rearWheelSprite, frontWheelSprite;
    private Mod_Carrier carrier;
    private bool offsetsCaptured;
    private bool facesLeft;
    #endregion

    #region 位移驱动与生命周期
    private void OnEnable()
    {
        carrier = GetComponentInChildren<Mod_Carrier>(true);
        ResetWheelPresentationMotionBaseline();
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.PhysicsPreparation);
    }

    private void OnDisable() => GameplayFixedStepSimulation.Unregister(this);

    public void SimulateFixedStep(float deltaTime)
    {
        if (carrier == null || !carrier.IsAvailable || !FlatWorld.Networking.GameNetwork.HasStateAuthority) return;
        using var world = carrier.item?.UseOwnedWorldForItemCallbacks();
        RefreshFacingFromRiderPointer();
    }

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
        WorldRenderingManager rendering = WorldRenderingManager.ExistingInstance;
        Vector2 position = rendering != null && carrier?.item != null
            ? rendering.GetItemRenderedWorldPosition(carrier.item) : transform.position;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(previousPosition, position);
        previousPosition = position;
        // 传送和重新投影只重设基线，不能把位置突变当作骑行距离。
        if (displacement.sqrMagnitude > 4f) return;
        AdvanceWheelRotationByDisplacement(displacement);
    }

    public void AdvanceWheelRotationByDisplacement(Vector2 displacement)
    {
        if (!offsetsCaptured || WheelRadius <= 0f || displacement.sqrMagnitude <= .00000001f) return;
        bool movingLeft = Mathf.Abs(displacement.x) > .0001f ? displacement.x < 0f : facesLeft;
        float wheelDelta = CalculateWheelRotationDegrees(displacement.magnitude, WheelRadius, movingLeft);
        WheelAngle = Mathf.Repeat(WheelAngle + wheelDelta, 360f);
        PedalAngle = Mathf.Repeat(PedalAngle + wheelDelta * PedalTurnsPerWheelTurn, 360f);
        ApplyWheelPose();
    }

    /// <summary>双轮落地点的中点固定为车头判向锚点，不再使用偏移的座位或车体原点。</summary>
    public Vector2 FacingGroundAnchorWorldPosition
    {
        get
        {
            Vector3 rear = RearWheel.position;
            Vector3 front = FrontWheel.position;
            return new Vector2((rear.x + front.x) * .5f, Mathf.Min(rear.y, front.y) - WheelRadius);
        }
    }

    /// <summary>屏幕中央与车底锚点各有死区，镜头跟随偏移时也不能反复翻面。</summary>
    public static bool IsPointerInsideFacingDeadZone(float pointerScreenX, float anchorScreenX,
        float screenCenterX, float deadZonePixels)
        => Mathf.Abs(pointerScreenX - screenCenterX) <= deadZonePixels ||
           Mathf.Abs(pointerScreenX - anchorScreenX) <= deadZonePixels;

    /// <summary>只在有本地骑手时读取统一光标；空车保持最后朝向，不随惯性或推动翻面。</summary>
    private void RefreshFacingFromRiderPointer()
    {
        if (carrier == null) carrier = GetComponentInChildren<Mod_Carrier>(true);
        if (carrier == null || !(carrier.Rider?.item is Item rider && ActorIdentity.IsPlayer(rider)) || !ActorProfileRegistry.IsLocallyControlled(rider)) return;
        Mod_GameController controller = rider.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller != null && controller.TryGetMouseWorldPosition(out Vector3 pointerWorld))
        {
            Vector2 anchor = FacingGroundAnchorWorldPosition;
            Vector2 direction = WorldTopologyRuntime.ShortestDelta(anchor, pointerWorld);
            Camera camera = controller._mainCamera;
            if (camera != null && IsPointerInsideFacingDeadZone(
                    camera.WorldToScreenPoint(anchor + direction).x,
                    camera.WorldToScreenPoint(anchor).x,
                    camera.pixelRect.center.x, FacingDeadZonePixels)) return;
            SetFacingFromPointerDirection(direction);
        }
    }

    /// <summary>光标越过死区后才按水平侧别翻面，光标重合时沿用上一朝向。</summary>
    public void SetFacingFromPointerDirection(Vector2 pointerDirection)
    {
        if (Mathf.Abs(pointerDirection.x) <= .001f) return;
        bool shouldFaceLeft = pointerDirection.x < 0f;
        if (facesLeft == shouldFaceLeft) return;
        facesLeft = shouldFaceLeft;
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
