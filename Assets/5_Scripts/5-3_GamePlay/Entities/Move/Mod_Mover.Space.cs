using UnityEngine;
using UnityEngine.InputSystem;
using FlatWorld.Spaceflight;

public partial class Mod_Mover
{
    #region 太空运动仲裁
    private InputAction spaceTurnAction;
    private bool spaceMotionOwned;
    private RigidbodyType2D beforeSpaceBodyType;
    public bool IsHoldRunInputActive => holdRunInputActive;
    private bool UpdateSpaceMotion(float seconds)
    {
        if (item is not Player player || !player.IsLocalProfile || SpaceSession.Current == null || rb == null) return false;
        Mod_GameController controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        Vector2 input = controller != null && !controller.IsGameplayInputLocked ? controller.ReadMoveInput(moveAction) : Vector2.zero;
        if (spaceTurnAction == null)
        {
            spaceTurnAction = new InputAction("SpaceTurn", InputActionType.Value);
            spaceTurnAction.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/e").With("Positive", "<Keyboard>/q");
            spaceTurnAction.Enable();
        }
        float turn = controller != null && !controller.IsGameplayInputLocked &&
            controller.IsGameplayInputAllowed(spaceTurnAction.activeControl?.device) ? spaceTurnAction.ReadValue<float>() : 0f;
        bool controlled = SpaceSession.Current.HandlePassengerInput(player, this, controller, input, turn, holdRunInputActive, seconds);
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
        else if (spaceMotionOwned)
        {
            rb.bodyType = beforeSpaceBodyType; spaceMotionOwned = false;
            DrivenVelocity = ExternalVelocity = Vector2.zero;
        }
        return controlled;
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
        spaceTurnAction?.Disable(); spaceTurnAction?.Dispose(); spaceTurnAction = null;
        if (spaceMotionOwned && rb != null) rb.bodyType = beforeSpaceBodyType;
        spaceMotionOwned = false;
    }
    #endregion
}
