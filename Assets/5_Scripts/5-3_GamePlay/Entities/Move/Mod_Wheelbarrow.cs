using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;

/// <summary>独轮车通过站立手推租约或车尾碰撞推动，货仓复用普通箱子面板。</summary>
public sealed class Mod_Wheelbarrow : Module, ICarrierMotionDriver, ICarrierPushMotionPolicy,
    ICarrierPushEligibilityPolicy, ICarrierTapInteractionPolicy, ICarrierOperatorPresentationPolicy, IItemModuleDependencyBinder
{
    #region 车体配置和生命周期
    public const string ModuleId = "Mod_Wheelbarrow";
    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public float PushSpeedMultiplier = .65f;
    public bool AllowsRiderBoost => false;
    public bool AnimateOperatorWalking => true;
    public Mod_Carrier Carrier { get; private set; }
    public Inventory CargoInventory => inventory?.CargoInventory;
    public bool IsAvailable => loaded && isActiveAndEnabled && Carrier != null && Carrier.IsAvailable;
    public Vector2 ForwardDirection => forwardDirection;
    private Mod_WheelbarrowInventory inventory;
    private Rigidbody2D body;
    private RigidbodyConstraints2D originalConstraints;
    private bool hasOriginalConstraints;
    private Player viewingPlayer;
    private bool loaded;
    private Vector2 forwardDirection = Vector2.down;
    private readonly RaycastHit2D[] obstructionHits = new RaycastHit2D[24];
    private static readonly ProfilerMarker CarrierMotionMarker = new("FlatWorld.Wheelbarrow.StepCarrierMotion");

    [Serializable] private sealed class WheelbarrowState
    {
        public int Version = 1;
        public float ForwardX;
        public float ForwardY = -1f;
    }

    public void BindModuleDependencies(ItemMods modules)
    {
        Carrier = modules.RequireSingleModById<Mod_Carrier>(Mod_Carrier.ModuleId);
        inventory = modules.RequireSingleModById<Mod_WheelbarrowInventory>(Mod_WheelbarrowInventory.ModuleId);
    }

    protected override void OnLoad()
    {
        if (!(PushSpeedMultiplier > 0f && PushSpeedMultiplier <= 1f) || !float.IsFinite(PushSpeedMultiplier))
            throw new InvalidOperationException("独轮车推动速度倍率必须在零到一之间。");
        WheelbarrowState state = Data.GetData<WheelbarrowState>() ?? new WheelbarrowState();
        if (state.Version != 1) throw new InvalidOperationException("独轮车快照版本无效。");
        Vector2 savedForward = new(state.ForwardX, state.ForwardY);
        forwardDirection = float.IsFinite(savedForward.sqrMagnitude) && savedForward.sqrMagnitude > .0001f
            ? savedForward.normalized : Vector2.down;
        loaded = true;
        if (Carrier != null && Carrier.IsAvailable && Carrier.Body != null)
        {
            ActivateCarrier(Carrier, Carrier.Body);
            Carrier.ApplyDrivenVelocity(Vector2.zero, -forwardDirection);
        }
    }

    protected override void OnSave()
        => Data.WriteData(new WheelbarrowState { ForwardX = forwardDirection.x, ForwardY = forwardDirection.y });
    protected override void OnUnload()
    {
        CloseCargoPanel(); Carrier?.Rider?.ReleaseCarrierLease(); loaded = false;
        Carrier?.StopMotion(); RestoreWheelbarrowBodyConstraints();
    }
    private void OnEnable()
    {
        if (loaded && Carrier != null && Carrier.IsAvailable && Carrier.Body != null)
            ActivateCarrier(Carrier, Carrier.Body);
    }
    private void OnDisable()
    { CloseCargoPanel(); Carrier?.Rider?.ReleaseCarrierLease(); Carrier?.StopMotion(); RestoreWheelbarrowBodyConstraints(); }
    private void OnDestroy() => Unload();
    #endregion

    #region 货仓交互
    public bool CanAccess(Player player)
    {
        if (!IsAvailable || !GameNetwork.HasStateAuthority || player == null || !player.IsLocalProfile ||
            player.DestructionHandled || player.gameObject.scene != item.gameObject.scene) return false;
        Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health == null || health.Hp <= 0f) return false;
        var senders = player.itemMods.GetCapabilities<Mod_InteractSender>();
        float distance = senders.Count == 0 ? Mod_InteractSender.DefaultMaxInteractDistance : senders[0].maxInteractDistance;
        return WorldTopologyRuntime.Distance(player.transform.position, item.transform.position) <= distance;
    }

    public bool CanInteractWithCarrier(Player player, Mod_Mover mover)
        => mover != null && (mover.CarrierSource == null && Carrier.Rider == null ||
                            ReferenceEquals(mover.CarrierSource, Carrier) && Carrier.Rider == mover) &&
           CargoInventory != null && CanAccess(player);

    public void InteractWithCarrier(Player player, Mod_Mover mover)
    {
        if (!CanInteractWithCarrier(player, mover)) return;
        CloseCargoPanel();
        if (Carrier.Rider == null)
        {
            Vector2 towardCart = WorldTopologyRuntime.ShortestDelta(player.transform.position, body.position);
            Vector2 previousForward = forwardDirection;
            if (towardCart.sqrMagnitude > .0001f) forwardDirection = towardCart.normalized;
            if (!CanOperatorStandAt(ResolveOperatorPosition(Carrier)) || !Carrier.ToggleRider(player, mover))
                forwardDirection = previousForward;
        }
        else Carrier.ToggleRider(player, mover);
        Carrier.ApplyDrivenVelocity(Vector2.zero, -forwardDirection);
    }

    public void InteractWithCarrierOnTap(Player player, Mod_Mover mover)
    {
        if (!CanInteractWithCarrier(player, mover) || Carrier.Rider != null) return;
        Inventory cargo = CargoInventory;
        Carrier.StopMotion();
        viewingPlayer = player;
        cargo.RuntimeAccessGuard = CanAccessOpenCargoPanel;
        cargo.DefaultTarget_Inventory = player.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        cargo.InventoryPanel_Prefab ??= GameRes.Instance.GetPrefab("UI_Bag");
        cargo.SwitchUI();
    }

    public Vector2 ResolveOperatorPosition(Mod_Carrier carrier)
        => (Vector2)item.transform.position - forwardDirection *
           ResolveOperatorDistance(carrier);

    private static float ResolveOperatorDistance(Mod_Carrier carrier)
        => Mathf.Max(carrier.PushHalfExtents.x, carrier.PushHalfExtents.y) + .4f;

    private bool CanOperatorStandAt(Vector2 position)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        return manager != null && manager.IsRuntimeWalkableLand(position) &&
               manager.IsRuntimeWalkableLand(position + Vector2.right * .2f) &&
               manager.IsRuntimeWalkableLand(position + Vector2.left * .2f) &&
               manager.IsRuntimeWalkableLand(position + Vector2.up * .2f) &&
               manager.IsRuntimeWalkableLand(position + Vector2.down * .2f);
    }

    private bool CanAccessOpenCargoPanel()
        => CargoInventory?.basePanel != null && CargoInventory.basePanel.IsOpen() && CanAccess(viewingPlayer);

    public override void ModUpdate(float deltaTime)
    {
        if (viewingPlayer != null && !CanAccessOpenCargoPanel()) CloseCargoPanel();
    }

    private void CloseCargoPanel()
    {
        Inventory cargo = CargoInventory;
        viewingPlayer = null;
        if (cargo == null) return;
        cargo.RuntimeAccessGuard = () => false;
        cargo.basePanel?.Close();
        cargo.DefaultTarget_Inventory = null;
        cargo.SyncQuickTransferTarget();
    }
    #endregion

    #region 后侧推动和碰撞
    public void ActivateCarrier(Mod_Carrier carrier, Rigidbody2D carrierBody)
    {
        if (body != carrierBody) RestoreWheelbarrowBodyConstraints();
        body = carrierBody;
        if (!hasOriginalConstraints)
        {
            originalConstraints = body.constraints;
            hasOriginalConstraints = true;
        }
        WorldPhysicsService2D.ApplyLegacyItemLinearDamping(item, body, 0f);
        body.useFullKinematicContacts = false;
        SetWheelbarrowPositionLocked(true);
        carrier.ApplyDrivenVelocity(Vector2.zero, -forwardDirection);
    }

    private void SetWheelbarrowPositionLocked(bool locked)
    {
        if (body == null || !hasOriginalConstraints) return;
        RigidbodyConstraints2D constraints = locked
            ? originalConstraints | RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY
            : originalConstraints;
        if (body.constraints != constraints) body.constraints = constraints;
    }

    private void RestoreWheelbarrowBodyConstraints()
    {
        if (body != null && hasOriginalConstraints) body.constraints = originalConstraints;
        hasOriginalConstraints = false;
        body = null;
    }

    public bool CanReceiveCarrierPush(Mod_Mover source)
    {
        if (!IsAvailable || Carrier.Rider != null || source == null || !source.isActiveAndEnabled || source.IsLock || source.rb == null ||
            !source.rb.simulated || source.item is not Player player || !player.IsLocalProfile || player.DestructionHandled ||
            source.gameObject.scene != item.gameObject.scene || body == null) return false;
        Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        Mod_Inventory bag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (controller == null || controller.IsGameplayInputLocked || health == null || health.Hp <= 0f ||
            bag?.IsPanelOpen == true || CargoInventory?.basePanel != null && CargoInventory.basePanel.IsOpen()) return false;
        Vector2 towardCart = WorldTopologyRuntime.ShortestDelta(source.rb.position, body.position);
        float maxDistance = Carrier.PushHalfExtents.magnitude + source.pushContactRadius + .25f;
        return towardCart.sqrMagnitude <= maxDistance * maxDistance &&
            IsPusherBehindWheelbarrow(forwardDirection, towardCart, source.RequestedMoveInput);
    }

    public static bool IsPusherBehindWheelbarrow(Vector2 forward, Vector2 towardCart, Vector2 input)
    {
        if (!float.IsFinite(forward.sqrMagnitude + towardCart.sqrMagnitude + input.sqrMagnitude) ||
            forward.sqrMagnitude < .0001f || towardCart.sqrMagnitude < .0001f || input.sqrMagnitude < .001f) return false;
        towardCart.Normalize();
        return Vector2.Dot(forward.normalized, towardCart) >= .65f && Vector2.Dot(input.normalized, towardCart) > .25f;
    }

    public Vector2 ResolveCarrierPush(Vector2 velocity, bool sourceInWater) => velocity * PushSpeedMultiplier;
    public Vector2 ResolvePusherLimit(Vector2 velocity) => velocity;

    public void StepCarrierMotion(Mod_Carrier carrier, Vector2 input, bool controlsLocked, float deltaTime)
    {
        if (!loaded || !isActiveAndEnabled) return;
        using var motionSample = CarrierMotionMarker.Auto();
        if (carrier.Rider != null && ReferenceEquals(carrier.Rider.CarrierSource, carrier))
        {
            StepOperatorPush(carrier, input, controlsLocked, deltaTime);
            return;
        }
        Mod_Mover source = carrier.PushSource;
        Vector2 pushing = carrier.PushedVelocity;
        if (source == null || !CanReceiveCarrierPush(source) || pushing.sqrMagnitude < .0001f || !(deltaTime > 0f))
        {
            SetWheelbarrowPositionLocked(true);
            carrier.ApplyDrivenVelocity(Vector2.zero, -forwardDirection);
            return;
        }
        Vector2 direction = WorldTopologyRuntime.ShortestDelta(source.rb.position, body.position).normalized;
        float speed = Mathf.Clamp(Vector2.Dot(pushing, direction), 0f, carrier.MaxSpeed);
        Vector2 velocity = direction * speed;
        float allowed = CalculateWheelbarrowMovementClearance(velocity, source.rb, deltaTime);
        if (allowed > 0f && speed > .0001f) forwardDirection = direction;
        SetWheelbarrowPositionLocked(allowed <= 0f || speed <= .0001f);
        carrier.ApplyDrivenVelocity(velocity * allowed, -forwardDirection);
    }

    /// <summary>抓住车把后复用载具速度和物理清障，玩家留在车尾步行，不坐进车斗。</summary>
    private void StepOperatorPush(Mod_Carrier carrier, Vector2 input, bool controlsLocked, float deltaTime)
    {
        if (controlsLocked || input.sqrMagnitude < .001f || !(deltaTime > 0f))
        {
            SetWheelbarrowPositionLocked(true);
            carrier.ApplyDrivenVelocity(Vector2.zero, -forwardDirection);
            return;
        }
        Vector2 heading = Vector3.RotateTowards(forwardDirection, input.normalized,
            carrier.FacingTurnSpeed * Mathf.Deg2Rad * deltaTime, 0f).normalized;
        Vector2 velocity = carrier.SmoothDrivenVelocity(carrier.DrivenVelocity,
            heading * (input.magnitude * PushSpeedMultiplier), false, false, deltaTime);
        float allowed = CanOperatorStandAt(body.position + velocity * deltaTime - heading * ResolveOperatorDistance(carrier))
            ? CalculateWheelbarrowMovementClearance(velocity, null, deltaTime) : 0f;
        if (allowed > 0f) forwardDirection = heading;
        SetWheelbarrowPositionLocked(allowed <= 0f);
        carrier.ApplyDrivenVelocity(velocity * allowed, -forwardDirection);
    }

    private float CalculateWheelbarrowMovementClearance(Vector2 velocity, Rigidbody2D pusher, float deltaTime)
    {
        float distance = velocity.magnitude * deltaTime;
        if (distance <= .000001f) return 0f;
        Vector2 direction = velocity.normalized;
        if (!Carrier.CanTraversePosition(body.position + velocity * deltaTime)) return 0f;
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true,
            layerMask = Physics2D.GetLayerCollisionMask(item.gameObject.layer) };
        int count = body.Cast(direction, filter, obstructionHits, distance + .002f);
        if (count >= obstructionHits.Length) return 0f;
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = obstructionHits[i];
            if (hit.collider == null || hit.collider.isTrigger || hit.rigidbody == body || hit.rigidbody == pusher ||
                hit.normal.sqrMagnitude > .01f && Vector2.Dot(direction, hit.normal) >= -.001f) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - .002f));
        }
        return Mathf.Clamp01(allowed / distance);
    }

    public void StopCarrierMotion()
    {
        if (body != null && body.simulated) body.velocity = Vector2.zero;
        SetWheelbarrowPositionLocked(true);
    }
    #endregion
}
