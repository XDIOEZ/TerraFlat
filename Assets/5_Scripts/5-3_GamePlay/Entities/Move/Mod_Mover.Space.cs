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
    public bool IsUsingPointerBodyRotation { get; private set; }

    public void SetPointerBodyRotation(bool enabled)
    {
        if (IsUsingPointerBodyRotation == enabled) return;
        IsUsingPointerBodyRotation = enabled;
        item?.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody)?.SetMirroringEnabled(!enabled);
        item?.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetFollowTargetRotation(!enabled);
    }

    private bool UpdateSpaceMotion(float seconds)
    {
        if (item is not Player player || !player.IsLocalProfile || SpaceSession.Current?.State == null || rb == null)
        {
            SetPointerBodyRotation(false);
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
            SetPointerBodyRotation(false);
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
        SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
        Vector2 input = Enabled && !IsLock && inputController != null && !inputController.IsGameplayInputLocked
            ? Vector2.ClampMagnitude(RequestedMoveInput, 1f) : Vector2.zero;
        if (inputController?._mainCamera != null)
            input = inputController._mainCamera.transform.TransformDirection(input);
        float speed = Mathf.Min(Mathf.Max(0f, Speed.Value), (float)settings.FreeFloatMaxSpeedMetersPerSecond);
        // 失重与冰面共用速度过渡，只降低响应倍率来延长加速、转向和松键滑行。
        return SmoothSurfaceVelocity(currentVelocity, input * speed, seconds,
            settings.FreeFloatAccelerationMultiplier, settings.FreeFloatDecelerationMultiplier);
    }

    private float ResolveSurfaceGravitySpeedMultiplier()
    {
        if (SpaceSession.Current?.State == null || SpaceSession.Current.IsSpaceView) return 1f;
        if (SpaceSession.Current.TryGetSupport(item.transform.position, out ShipState support, out _, out Vector2 local) &&
            SpaceSession.Current.HasArtificialGravity(support, local.x, local.y)) return 1f;
        var body = SpaceSession.Current.FindSurfaceBody(item.gameObject.scene.name);
        return body != null ? (float)SpaceGameplaySettings.Current.WalkingMultiplier(body.SurfaceGravity) : 1f;
    }
    private void ReleaseSpaceInput()
    {
        SetPointerBodyRotation(false);
        spaceTurnAction?.Disable(); spaceTurnAction?.Dispose(); spaceTurnAction = null;
        if (spaceMotionOwned && rb != null) rb.bodyType = beforeSpaceBodyType;
        spaceMotionOwned = false;
    }
    #endregion
}
