using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>矿车只消费通用动力和轨道路径，座位与玩家输入继续由统一载具管理。</summary>
public sealed partial class Mod_Minecart : Module, ICarrierMotionDriver, ICarrierPushMotionPolicy, IItemModuleDependencyBinder,
    IBuildingPlacementCommitted, IBuildingDismantlePolicy
{
    #region 矿车配置和关联
    public const string ModuleId = "Mod_Minecart";
    public Ex_ModData Data = new();
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public float MaxSpeed = 5f;
    public float Acceleration = 3f;
    public float Braking = 6f;
    public float PowerPerCart = 1f;
    public float ManualPower = .25f;
    public float ManualStaminaPerSecond = 5f;
    public float CouplingDistance = 1.75f;
    public float GroundSpeedMultiplier = .025f;
    public float GroundBraking = 60f;
    public float RailCaptureDistance = DefaultRailCaptureDistance;
    public float PushSpeedMultiplier = 1f;
    public bool AllowsRiderBoost => false;
    public Mod_Carrier Carrier { get; private set; }
    public Inventory EngineInventory => inventory?.EngineInventory;
    public Inventory CargoInventory => inventory?.CargoInventory;
    public bool IsAvailable => loaded && isActiveAndEnabled && Carrier != null && Carrier.IsAvailable;
    public bool IsLinked => state != null && (state.PreviousGuid != 0 || state.NextGuid != 0);
    public int TrainCount => train?.Cars.Count ?? 1;
    public static event Action<Mod_Minecart, Player> MenuRequested;
    private Mod_MinecartInventory inventory;
    private MinecartState state;
    private Mod_Minecart previous, next;
    private MinecartTrain train;
    private RailPosition railPosition;
    private bool loaded;
    private Vector2 requestedInput;
    private Vector2 heldDrivingDirection;
    private float heldDrivingSign;
    private bool controlsLocked = true;
    private float lastInputAt = float.NegativeInfinity;
    private readonly RaycastHit2D[] contactHits = new RaycastHit2D[24];
    private DistanceJoint2D coupler;
    private bool onRail;
    private Vector2 motionHeading = Vector2.up;
    private static readonly Dictionary<(int scene, int guid), Mod_Minecart> carts = new();
    [Serializable] public sealed class MinecartState
    { public int Version = 1; public int PreviousGuid; public int NextGuid; public float PreviousSpacing; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { carts.Clear(); MenuRequested = null; }

    public void BindModuleDependencies(ItemMods modules)
    {
        Carrier = modules.RequireSingleModById<Mod_Carrier>(Mod_Carrier.ModuleId);
        inventory = modules.RequireSingleModById<Mod_MinecartInventory>(Mod_MinecartInventory.ModuleId);
    }

    protected override void OnLoad()
    {
        if (!(MaxSpeed > 0f && Acceleration > 0f && Braking > 0f && PowerPerCart > 0f &&
            ManualPower > 0f && ManualStaminaPerSecond > 0f &&
            CouplingDistance >= .8f && GroundSpeedMultiplier > 0f && GroundSpeedMultiplier <= 1f &&
            GroundBraking > 0f && RailCaptureDistance > 0f && RailCaptureDistance < .5f && PushSpeedMultiplier > 0f) ||
            !float.IsFinite(MaxSpeed + Acceleration + Braking + PowerPerCart + ManualPower + ManualStaminaPerSecond + CouplingDistance +
                GroundSpeedMultiplier + GroundBraking + RailCaptureDistance + PushSpeedMultiplier))
            throw new InvalidOperationException("矿车速度、动力和连接距离配置无效。");
        state = Data.GetData<MinecartState>() ?? new MinecartState();
        if (state.Version != 1) throw new InvalidOperationException("矿车快照版本无效。");
        loaded = true; train = new MinecartTrain(this); Register();
    }
    protected override void OnSave()
    {
        if (state != null) Data.WriteData(state);
    }
    public void OnBuildingPlacementCommitted() { Register(); SnapToRail(); }
    private void OnEnable() { if (loaded) Register(); }
    private void OnDisable() { if (loaded) ReleaseRuntime(false); }
    private void OnDestroy() => Unload();
    protected override void OnUnload()
    { ReleaseRuntime(item == null || item.DestructionHandled); loaded = false; }

    private void Register()
    {
        if (!loaded || item?.itemData == null || item.itemData.Guid == 0) return;
        var key = (item.gameObject.scene.handle, item.itemData.Guid);
        if (carts.TryGetValue(key, out Mod_Minecart existing) && existing != null && existing != this)
            throw new InvalidOperationException("矿车运行时身份重复。");
        carts[key] = this;
        RestoreConnections();
    }

    private void ReleaseRuntime(bool clearLinks)
    {
        ReleaseCoupler(); next?.ReleaseCoupler();
        IgnoreTrainContacts(train, false);
        if (clearLinks && item?.itemData != null)
        {
            int scene = item.gameObject.scene.handle;
            if (previous == null && state.PreviousGuid != 0) carts.TryGetValue((scene, state.PreviousGuid), out previous);
            if (next == null && state.NextGuid != 0) carts.TryGetValue((scene, state.NextGuid), out next);
        }
        if (item?.itemData != null) carts.Remove((item.gameObject.scene.handle, item.itemData.Guid));
        StopCarrierMotion();
        if (previous != null) { previous.next = null; if (clearLinks) previous.state.NextGuid = 0; Rebuild(previous); IgnoreTrainContacts(previous.train, true); }
        if (next != null) { next.previous = null; if (clearLinks) next.state.PreviousGuid = 0; Rebuild(next); IgnoreTrainContacts(next.train, true); }
        previous = next = null;
        if (clearLinks && state != null) state.PreviousGuid = state.NextGuid = 0;
        train = new MinecartTrain(this);
    }

    private void RestoreConnections()
    {
        if (state == null || item == null) return;
        int scene = item.gameObject.scene.handle, guid = item.itemData.Guid;
        if (previous == null && state.PreviousGuid != 0 && carts.TryGetValue((scene, state.PreviousGuid), out Mod_Minecart before) &&
            before != this && before.state?.NextGuid == guid && before.next == null)
        { previous = before; before.next = this; }
        if (next == null && state.NextGuid != 0 && carts.TryGetValue((scene, state.NextGuid), out Mod_Minecart after) &&
            after != this && after.state?.PreviousGuid == guid && after.previous == null)
        { next = after; after.previous = this; }
        if (previous == null && state.PreviousGuid != 0 && carts.TryGetValue((scene, state.PreviousGuid), out Mod_Minecart oldBefore) &&
            oldBefore.state?.NextGuid != guid) state.PreviousGuid = 0;
        if (next == null && state.NextGuid != 0 && carts.TryGetValue((scene, state.NextGuid), out Mod_Minecart oldAfter) &&
            oldAfter.state?.PreviousGuid != guid) state.NextGuid = 0;
        Rebuild(this);
        IgnoreTrainContacts(train, true);
    }
    #endregion

    #region 统一座位和交互命令
    public bool CanAccess(Player player)
        => IsAvailable && GameNetwork.HasStateAuthority && player != null && player.IsLocalProfile &&
            player.gameObject.scene == item.gameObject.scene &&
            (Carrier.Rider?.item == player || WorldTopologyRuntime.Distance(player.transform.position, item.transform.position) <=
                ResolveInteractionDistance(player));

    public static bool IsPlacementClear(Item host, Vector2 position)
    {
        if (host == null) return false;
        foreach (Mod_Minecart cart in carts.Values)
            if (cart != null && cart.IsAvailable && cart.item.gameObject.scene == host.gameObject.scene &&
                WorldTopologyRuntime.Distance(cart.item.transform.position, position) < .8f) return false;
        return true;
    }

    private static float ResolveInteractionDistance(Player player)
    {
        var senders = player.itemMods.GetCapabilities<Mod_InteractSender>();
        return senders.Count == 0 ? Mod_InteractSender.DefaultMaxInteractDistance : senders[0].maxInteractDistance;
    }

    public bool CanInteractWithCarrier(Player player, Mod_Mover mover)
        => CanAccess(player) && mover != null;

    public void InteractWithCarrier(Player player, Mod_Mover mover)
    {
        if (!CanInteractWithCarrier(player, mover)) return;
        if (Carrier.Rider == mover) { TryLeave(player); return; }
        if (Carrier.Rider == null && mover.CarrierSource == null) TryBoard(player);
        MenuRequested?.Invoke(this, player);
    }
    public bool TryBoard(Player player)
    {
        if (!CanAccess(player) || Carrier.Rider != null) return false;
        Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        return mover != null && Carrier.ToggleRider(player, mover);
    }
    public bool TryLeave(Player player)
    {
        if (!CanAccess(player)) return false;
        Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        return mover != null && Carrier.Rider == mover && Carrier.ToggleRider(player, mover);
    }
    public bool TryConnectNearest(Player player, out string reason)
    {
        reason = "没有找到同一铁路上可连接的相邻矿车。";
        if (!CanAccess(player)) return false;
        if (HasUnresolvedConnection) { reason = "关联矿车尚未加载，请等它加载或先断开连接。"; return false; }
        if (previous != null && next != null) { reason = "这节矿车的两端已经连接。"; return false; }
        Mod_Minecart nearest = null; float best = CouplingDistance;
        foreach (Mod_Minecart candidate in carts.Values)
        {
            if (candidate == null || candidate == this || !candidate.IsAvailable || candidate.HasUnresolvedConnection || candidate.train == train ||
                candidate.item.gameObject.scene != item.gameObject.scene || candidate.previous != null && candidate.next != null ||
                !TryCouplingDistance(candidate, best, out float distance)) continue;
            if (distance < .8f || distance > best) continue;
            nearest = candidate; best = distance;
        }
        if (nearest == null) return false;
        return Connect(nearest, out reason);
    }

    private bool Connect(Mod_Minecart other, out string reason)
    {
        reason = "只有静止的相邻列车端点才能连接。";
        if (other == null || other.train == train || !IsAvailable || !other.IsAvailable ||
            Mathf.Abs(train.Speed) > .05f || Mathf.Abs(other.train.Speed) > .05f ||
            previous != null && next != null || other.previous != null && other.next != null ||
            !TryCouplingDistance(other, CouplingDistance, out float distance) || distance < .8f) return false;
        if (next != null) ReverseTrain(train);
        if (other.previous != null) ReverseTrain(other.train);
        next = other; other.previous = this;
        other.state.PreviousSpacing = distance;
        state.NextGuid = other.item.itemData.Guid; other.state.PreviousGuid = item.itemData.Guid;
        Rebuild(this);
        IgnoreTrainContacts(train, true);
        reason = null; return true;
    }

    private bool HasUnresolvedConnection => state != null &&
        (state.PreviousGuid != 0 && previous == null || state.NextGuid != 0 && next == null);

    public void Disconnect(Player player)
    {
        if (!CanAccess(player)) return;
        DisconnectAll();
    }
    private void DisconnectAll()
    {
        ReleaseCoupler(); next?.ReleaseCoupler();
        IgnoreTrainContacts(train, false); StopCarrierMotion();
        Mod_Minecart before = previous, after = next;
        if (before != null) { before.next = null; before.state.NextGuid = 0; }
        if (after != null) { after.previous = null; after.state.PreviousGuid = 0; }
        previous = next = null; state.PreviousGuid = state.NextGuid = 0;
        Rebuild(this); if (before != null) Rebuild(before); if (after != null) Rebuild(after);
        if (before != null) IgnoreTrainContacts(before.train, true);
        if (after != null) IgnoreTrainContacts(after.train, true);
    }
    public bool CanDismantle(out string reason)
    {
        bool valid = Carrier?.Rider == null && !IsLinked;
        reason = valid ? null : "请先下车并断开矿车连接。";
        return valid;
    }
    #endregion

}
