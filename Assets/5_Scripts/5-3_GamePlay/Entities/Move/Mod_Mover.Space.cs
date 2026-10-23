using UnityEngine;
using UnityEngine.InputSystem;
using FlatWorld.Spaceflight;

public partial class Mod_Mover
{
    #region 太空运动仲裁
    private InputAction spaceTurnAction;
    private bool spaceMotionOwned;
    private RigidbodyType2D beforeSpaceBodyType;
    private bool IsSpaceRunningDisabled => item is Player && SpaceSession.Current?.IsSpaceView == true;
    private bool spaceUpright;
    private bool beforeSpaceSimulated;

    public void SetSpaceUpright(bool enabled)
    {
        if (spaceUpright != enabled)
        {
            spaceUpright = enabled;
            if (rb != null)
            {
                if (enabled) { beforeSpaceSimulated = rb.simulated; rb.simulated = false; }
                else rb.simulated = beforeSpaceSimulated;
            }
            // 模式切换时才同步镜头设置，避免普通地面移动每帧重复查找相机模块。
            item?.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody)?.SetMirroringEnabled(true);
            item?.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetFollowTargetRotation(!enabled);
        }
        if (!enabled || item == null) return;
        // 太空里固定玩家与镜头朝上，鼠标仍可控制左右朝向与手持物瞄准。
        item.transform.rotation = Quaternion.identity;
        if (rb != null) { rb.rotation = 0f; rb.angularVelocity = 0f; }
    }

    private bool UpdateSpaceMotion(float seconds)
    {
        if (IsSpaceRunningDisabled) ReleaseMobileMovementInput();
        if (item is Player replica && !replica.IsLocalProfile && SpaceSession.Current != null &&
            SpaceSession.Current.TryGetNetworkObserver(replica.ProfileName, out SpaceNetworkObserver observer) && observer.InSpace)
        {
            SetSpaceUpright(true);
            return true;
        }
        if (item is not Player player || !player.IsLocalProfile || SpaceSession.Current?.State == null || rb == null)
        {
            SetSpaceUpright(false);
            return false;
        }
        if (IsSpaceRunningDisabled) SetRunState(false);
        Mod_GameController controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        Vector2 input = controller != null && !controller.IsGameplayInputLocked ? controller.ReadMoveInput(moveAction) : Vector2.zero;
        bool driving = !string.IsNullOrWhiteSpace(SpaceSession.Current.GetPassenger(player).ConsolePieceId);
        if (driving && spaceTurnAction == null)
        {
            spaceTurnAction = new InputAction("SpaceTurn", InputActionType.Value);
            spaceTurnAction.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/e").With("Positive", "<Keyboard>/q");
            spaceTurnAction.Enable();
        }
        float turn = driving && controller != null && !controller.IsGameplayInputLocked &&
            controller.IsGameplayInputAllowed(spaceTurnAction.activeControl?.device) ? spaceTurnAction.ReadValue<float>() : 0f;
        bool controlled = SpaceSession.Current.HandlePassengerInput(player, this, controller, input, turn, seconds);
        if (controlled)
        {
            if (!spaceMotionOwned) { beforeSpaceBodyType = rb.bodyType; spaceMotionOwned = true; }
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f; rb.drag = 0f; rb.angularDrag = 0f;
            RequestedMoveInput = input;
            DrivenVelocity = input * Speed.Value;
            ExternalVelocity = Vector2.zero;
            hungerActionInstance?.SetMovementState(input.sqrMagnitude > .001f, IsRunning);
            UpdateMovementState();
        }
        else
        {
            SetSpaceUpright(false);
            if (spaceMotionOwned)
            {
                rb.bodyType = beforeSpaceBodyType; spaceMotionOwned = false;
                DrivenVelocity = ExternalVelocity = Vector2.zero;
            }
        }
        return controlled;
    }

    public Vector2 SmoothSpaceVelocity(Vector2 currentVelocity, float seconds)
    {
        Vector2 input = Enabled && !IsLock && inputController != null && !inputController.IsGameplayInputLocked
            ? Vector2.ClampMagnitude(RequestedMoveInput, 1f) : Vector2.zero;
        if (inputController?._mainCamera != null)
            input = inputController._mainCamera.transform.TransformDirection(input);
        return SmoothSpaceVelocity(currentVelocity, seconds, input);
    }

    public Vector2 SmoothSpaceVelocity(Vector2 currentVelocity, float seconds, Vector2 input)
    {
        SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
        input = Vector2.ClampMagnitude(input, 1f);
        float speed = Mathf.Min(Mathf.Max(0f, Speed.Value), (float)settings.FreeFloatMaxSpeedMetersPerSecond);
        // 失重移动只在按键时累加速度，松键没有摩擦，反向输入才会制动。
        if (input.sqrMagnitude <= InputMoveThresholdSqr) return currentVelocity;
        float acceleration = speed / Mathf.Max(MinimumTransitionDuration, speedTransitionDuration) *
            settings.FreeFloatAccelerationMultiplier * PlayerAccelerationMultiplier;
        Vector2 nextVelocity = currentVelocity + input * acceleration * Mathf.Max(0f, seconds);
        return Vector2.ClampMagnitude(nextVelocity, Mathf.Max(speed, currentVelocity.magnitude));
    }

    /// <summary>导航读取包含当地重力的目标移速，环境推动另行叠加。</summary>
    public float SurfaceMoveSpeed => Mathf.Max(0f, Speed.Value) * ResolveSurfaceGravitySpeedMultiplier();

    private float ResolveSurfaceGravitySpeedMultiplier()
        => (float)SpaceGameplaySettings.Current.WalkingMultiplier(ResolveSurfaceGravity());

    private double ResolveSurfaceGravity()
    {
        double normalGravity = SpaceGameplaySettings.Current.EarthGravity;
        SpaceSession session = SpaceSession.Current;
        if (item == null || session?.State == null || SpaceMgr.IsSpaceScene(item.gameObject.scene.name))
            return normalGravity;
        if (session.TryGetSupport(item.transform.position, out ShipState support, out _, out Vector2 local) &&
            session.HasArtificialGravity(support, local.x, local.y)) return normalGravity;
        return session.FindSurfaceBody(item.gameObject.scene.name)?.SurfaceGravity ?? normalGravity;
    }
    private void ReleaseSpaceInput()
    {
        SetSpaceUpright(false);
        spaceTurnAction?.Disable(); spaceTurnAction?.Dispose(); spaceTurnAction = null;
        if (spaceMotionOwned && rb != null) rb.bodyType = beforeSpaceBodyType;
        spaceMotionOwned = false;
    }
    #endregion
}
