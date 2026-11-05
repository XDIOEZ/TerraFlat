using System;
using System.Linq;
using UnityEngine;

public interface IActorLocalControlModule { }
public interface IActorProfileModule { }
public interface IActorManualMotionOverride { bool ConsumesManualMovement { get; } }

/// <summary>同一 AI 角色的独占人工控制租约；释放只恢复自主决策，不解除档案归属。</summary>
public sealed class Mod_ActorControl : Module
{
    #region 控制租约
    public const string ModuleId = "Actor.Control";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public Func<Vector3, bool> NetworkBodyAttackRequest;
    private object controlOwner;
    private uint leaseVersion;
    public bool HasManualControl => ActorProfileRegistry.IsManuallyControlled(item);

    public bool TryAcquireManualControl(object owner, out IDisposable lease)
    {
        return TryAcquireManualControl(owner, true, out lease);
    }

    public bool TryAcquireManualControl(object owner, bool localControl, out IDisposable lease)
    {
        lease = null;
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (controlOwner != null && !ReferenceEquals(controlOwner, owner)) return false;
        controlOwner = owner;
        uint version = ++leaseVersion;
        Item actor = item != null ? item : GetComponentInParent<Item>();
        ActorProfileRegistry.SetManualControl(actor, true, localControl: localControl);
        lease = new ManualControlLease(this, version);
        return true;
    }

    public void ReleaseManualControl()
    {
        controlOwner = null;
        ++leaseVersion;
        ActorProfileRegistry.SetManualControl(item != null ? item : GetComponentInParent<Item>(), false);
    }

    private sealed class ManualControlLease : IDisposable
    {
        private Mod_ActorControl owner;
        private readonly uint version;
        public ManualControlLease(Mod_ActorControl owner, uint version) { this.owner = owner; this.version = version; }
        public void Dispose()
        {
            var current = owner;
            owner = null;
            if (current != null && current.leaseVersion == version) current.ReleaseManualControl();
        }
    }
    #endregion

    #region 模块生命周期
    protected override void OnLoad() => ApplyControlState(HasManualControl, false);
    public override void ModUpdate(float deltaTime)
    {
        if (!ActorProfileRegistry.IsLocallyControlled(item)) return;
        var controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller?.IsAttackHeld != true || controller.IsGameplayInputLocked) return;
        Item held = item.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        if (held != null && held.itemMods.GetCapabilities<IWeaponActionAnimation>().Count > 0) return;
        RequestConfiguredBodyAttack(controller.GetMouseWorldPosition());
    }
    public bool RequestConfiguredBodyAttack(Vector3 target)
    {
        if (NetworkBodyAttackRequest != null) return NetworkBodyAttackRequest(target);
        return item.itemMods.GetMod_ByID<Mod_AnimalBehaviorTree>(Mod_AnimalBehaviorTree.ModuleId)?.TryStartManuallyRequestedBodyAttack(target) == true;
    }
    public Vector2 ReadManualMovementInput()
    {
        if (!ActorProfileRegistry.IsLocallyControlled(item)) return Vector2.zero;
        var controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller?.IsGameplayInputLocked != false) return Vector2.zero;
        Vector2 input = controller.ReadMoveInput(controller._inputActions?.Win10.Move_Player);
        if (controller._mainCamera != null) input = controller._mainCamera.transform.TransformDirection(input);
        return Vector2.ClampMagnitude(BodyTraumaBuffEffects.TransformMoveInput(item, input, Time.time), 1f);
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        controlOwner = null;
        ++leaseVersion;
        ApplyControlState(false, true);
    }

    internal void ApplyControlState(bool manual, bool terminating)
    {
        Item actor = item != null ? item : GetComponentInParent<Item>();
        if (actor == null) return;
        var brain = actor.itemMods?.GetMod_ByID<Mod_AnimalBehaviorTree>(Mod_AnimalBehaviorTree.ModuleId)
            ?? actor.GetComponentInChildren<Mod_AnimalBehaviorTree>(true);
        brain?.SetAutonomousControlSuspended(manual || terminating);
        if (!manual)
            actor.itemMods?.GetCapabilities<Mod_Mover>().FirstOrDefault()?.ResetMotion();
    }
    #endregion
}

public static class ActorControlModuleLifecycle
{
    #region 控制范围内模块的受控启停
    public static bool CanLoad(Module module)
        => (module is not IActorLocalControlModule || ActorProfileRegistry.IsLocallyControlled(module.item)) &&
           (module is not IActorProfileModule || ActorProfileRegistry.IsProfileBound(module.item));

    internal static void RefreshLocalModules(Item actor)
    {
        if (!actor.IsInitialized) return;
        var controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        bool local = ActorProfileRegistry.IsLocallyControlled(actor);
        if (local) controller?.LoadRuntime();
        foreach (Module module in local ? actor.Mods.Values : actor.Mods.Values.Reverse())
        {
            if (module == controller) continue;
            if (module is IActorProfileModule)
            {
                if (ActorProfileRegistry.IsProfileBound(actor) && module.Enabled) module.LoadRuntime();
                else module.UnloadRuntime();
            }
            if (module is not IActorLocalControlModule) continue;
            if (local && module.Enabled) module.LoadRuntime();
            else module.UnloadRuntime();
        }
        if (!local) controller?.UnloadRuntime();
    }
    #endregion
}
