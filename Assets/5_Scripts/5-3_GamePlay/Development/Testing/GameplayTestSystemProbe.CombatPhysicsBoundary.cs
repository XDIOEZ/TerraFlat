using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 战斗物理与表现边界回归
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterCombatPhysicsBoundaryRules()
    {
        Register("combat", "hit_window_identity", CombatHitWindowIdentity);
        Register("combat", "physics_hit_ownership", CombatPhysicsHitOwnership);
        Register("combat", "damage_number_ownership", CombatDamageNumberOwnershipRules);
        Register("combat", "physics_region_delivery", CombatPhysicsRegionDelivery);
        Register("combat", "attack_window_order", CombatAttackWindowOrder);
    }

    private static JObject CombatHitWindowIdentity(JObject args)
    {
        var window = new CombatHitWindow();
        var identity = new CombatIdentity { Backend = CombatBackend.External, Value = 7, Generation = 2, World = 3, Dimension = 4 };
        window.Begin();
        uint sequence = window.Sequence;
        bool first = window.TryReserve(identity, 4);
        bool duplicate = window.TryReserve(identity, 4);
        CombatIdentity pooled = identity; pooled.Generation++;
        CombatIdentity otherWorld = identity; otherWorld.World++;
        CombatIdentity otherDimension = identity; otherDimension.Dimension++;
        bool isolated = window.TryReserve(pooled, 4) && window.TryReserve(otherWorld, 4) && window.TryReserve(otherDimension, 4);
        bool full = window.Remaining(4) == 0;
        window.Begin();
        bool nextWindow = window.Sequence != sequence && window.TryReserve(identity, 4);
        window.Reset();
        bool invalid = window.TryReserve(default, 4);
        return new JObject
        {
            ["firstAccepted"] = first, ["duplicateAccepted"] = duplicate, ["identityIsolated"] = isolated,
            ["quotaExhausted"] = full, ["nextWindowAccepted"] = nextWindow,
            ["invalidAccepted"] = invalid, ["resetCount"] = window.Count, ["resetSequence"] = window.Sequence
        };
    }

    private static JObject CombatAttackWindowOrder(JObject args)
    {
        var root = new GameObject("Attack window observation fixture"); root.SetActive(false);
        try
        {
            var collider = root.AddComponent<BoxCollider2D>();
            var damage = root.AddComponent<Mod_Damage>();
            damage.MaxAttackTargets = 2;
            damage.OnItemPhysicsComponentsAvailable();
            damage.SetExplicitProjectileSweep(true);
            damage.StartAttack();
            var target = new CombatIdentity { Backend = CombatBackend.External, Value = 1 };
            bool first = damage.TryReserveExternalTarget(target);
            bool notReopened = !damage.UpdateDamageWindowState() && damage.RemainingAttackTargets == 1;
            damage.OnItemPhysicsComponentsAvailable();
            bool bindingPreserved = !damage.UpdateDamageWindowState() && !damage.TryReserveExternalTarget(target);
            collider.enabled = false;
            damage.UpdateDamageWindowState();
            bool closed = damage.RemainingAttackTargets == 2;
            collider.enabled = true;
            bool reopened = damage.UpdateDamageWindowState() && damage.TryReserveExternalTarget(target);
            damage.StartAttack();
            bool nextAttack = damage.TryReserveExternalTarget(target) && !damage.UpdateDamageWindowState();
            damage.StopAttack();
            return new JObject { ["firstAccepted"] = first, ["notReopened"] = notReopened,
                ["bindingPreserved"] = bindingPreserved, ["closed"] = closed,
                ["reopened"] = reopened, ["nextAttack"] = nextAttack };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static JObject CombatPhysicsHitOwnership(JObject args)
    {
        using var space = WorldPhysicsScene2D.CreateIsolated("Combat ownership " + Guid.NewGuid().ToString("N"));
        using var physics = new WorldPhysicsService2D(space.Scene.name, WorldPhysicsScene2D.Borrow(space.Scene));
        var root = new GameObject("Combat ownership fixture");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, space.Scene);
        try
        {
            var item = root.AddComponent<GameItem>();
            var parentHealth = root.AddComponent<Mod_DamageReceiver>();
            parentHealth.modData = new Ex_ModData();
            parentHealth.BindRuntimeIdentity("primary-health", ModText.Hp, "probe:primary-health");
            item.itemMods.AddMod(parentHealth);
            var sibling = new GameObject("Sibling health"); sibling.transform.SetParent(root.transform, false);
            var selected = sibling.AddComponent<Mod_DamageReceiver>();
            var shapeRoot = new GameObject("Unambiguous hit region"); shapeRoot.transform.SetParent(root.transform, false);
            var shape = shapeRoot.AddComponent<BoxCollider2D>();
            shape.gameObject.layer = CombatPhysicsChannels.DamageReceiverLayer;
            bool noHierarchyGuess = GameplayCombatBridge.ResolvePhysicsDamageReceiver(shape) == null;
            physics.RegisterCollider(shape, item, selected, "selected", WorldPhysicsPurpose2D.Damage);
            bool exact = GameplayCombatBridge.ResolvePhysicsDamageReceiver(shape) == selected;
            var imageRoot = new GameObject("Mirror hit region"); imageRoot.transform.SetParent(root.transform, false);
            var image = imageRoot.AddComponent<BoxCollider2D>();
            imageRoot.AddComponent<ColliderSource2D>().Bind(shape, Vector2.right);
            bool mirror = GameplayCombatBridge.ResolvePhysicsDamageReceiver(image) == selected;
            physics.RegisterCollider(shape, item, parentHealth, purpose: WorldPhysicsPurpose2D.Blocking);
            bool nonDamageRejected = GameplayCombatBridge.ResolvePhysicsDamageReceiver(shape) == null;
            bool bodyMapped = GameplayCombatBridge.ResolvePhysicsBodyDamageReceiver(shape) == parentHealth &&
                GameplayCombatBridge.ResolvePhysicsBodyDamageReceiver(image) == parentHealth;
            shape.isTrigger = true;
            bool triggerBodyRejected = GameplayCombatBridge.ResolvePhysicsBodyDamageReceiver(shape) == null;
            shape.isTrigger = false;
            physics.RegisterCollider(shape, item, selected, purpose: WorldPhysicsPurpose2D.Interaction);
            bool interactionBodyRejected = GameplayCombatBridge.ResolvePhysicsBodyDamageReceiver(shape) == null;
            var native = new CombatPhysicsDamageTargetProbe();
            physics.RegisterCollider(shape, entity: native, purpose: WorldPhysicsPurpose2D.Damage);
            bool nativeMapped = GameplayCombatBridge.TryGetPhysicsDamageTarget(image, out var mapped) && ReferenceEquals(mapped, native);
            bool noFakeHealth = GameplayCombatBridge.ResolvePhysicsDamageReceiver(shape) == null;
            physics.RegisterCollider(shape, entity: native, purpose: WorldPhysicsPurpose2D.Blocking);
            bool nativeBodyMapped = !GameplayCombatBridge.TryGetPhysicsDamageTarget(shape, out _) &&
                GameplayCombatBridge.TryGetPhysicsContactDamageTarget(image, out var contactTarget) &&
                ReferenceEquals(contactTarget, native);
            physics.RegisterCollider(shape, entity: native, purpose: WorldPhysicsPurpose2D.Interaction);
            bool nativeInteractionRejected = !GameplayCombatBridge.TryGetPhysicsDamageTarget(shape, out _) &&
                !GameplayCombatBridge.TryGetPhysicsContactDamageTarget(shape, out _);
            physics.UnregisterCollider(shape);
            bool released = !GameplayCombatBridge.TryGetPhysicsDamageTarget(image, out _) &&
                GameplayCombatBridge.ResolvePhysicsDamageReceiver(image) == null;
            physics.RegisterCollider(shape, item, selected, "new-lease", WorldPhysicsPurpose2D.Damage);
            bool rebound = GameplayCombatBridge.ResolvePhysicsDamageReceiver(image) == selected;
            physics.UnregisterCollider(shape);
            return new JObject
            {
                ["noHierarchyGuess"] = noHierarchyGuess, ["exactReceiver"] = exact, ["mirrorReceiver"] = mirror,
                ["nonDamageRejected"] = nonDamageRejected, ["nativeMapped"] = nativeMapped,
                ["bodyMapped"] = bodyMapped, ["triggerBodyRejected"] = triggerBodyRejected,
                ["interactionBodyRejected"] = interactionBodyRejected, ["nativeBodyMapped"] = nativeBodyMapped,
                ["noFakeHealth"] = noFakeHealth, ["nativeInteractionRejected"] = nativeInteractionRejected,
                ["released"] = released, ["rebound"] = rebound
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static JObject CombatDamageNumberOwnershipRules(JObject args)
    {
        var root = new GameObject("Damage presentation fixture"); root.SetActive(false);
        var presenter = new CombatNumberPresenterProbe();
        Mod_DamageReceiver health = null;
        try
        {
            health = root.AddComponent<Mod_DamageReceiver>();
            var first = root.AddComponent<DamageTextEffect>();
            var secondRoot = new GameObject("Second number"); secondRoot.transform.SetParent(root.transform, false);
            var second = secondRoot.AddComponent<DamageTextEffect>();
            var effects = new List<GameEffect> { first, first, second };
            var selected = new List<GameEffect>();
            CombatImpactPresentation.CollectEffects(null, effects, CombatDamageKind.Blunt, true, selected);
            int numbers = selected.Count;
            CombatImpactPresentation.CollectEffects(null, effects, CombatDamageKind.Blunt, false, selected);
            int suppressed = selected.Count;
            CombatDamageNumberOwnership.Register(health, presenter);
            bool owned = CombatDamageNumberOwnership.OwnsNumber(health, 10f);
            bool blockedUnclaimed = !CombatDamageNumberOwnership.OwnsNumber(health, 0f);
            bool rejectedUnclaimed = !CombatDamageNumberOwnership.OwnsNumber(health, -1f);
            presenter.CanPresentDamageNumber = false;
            bool disabledReleased = !CombatDamageNumberOwnership.OwnsNumber(health, 10f);
            var replacement = new CombatNumberPresenterProbe();
            CombatDamageNumberOwnership.Register(health, replacement);
            CombatDamageNumberOwnership.Unregister(health, presenter);
            bool oldLeaseKeptNew = CombatDamageNumberOwnership.OwnsNumber(health, 10f);
            CombatDamageNumberOwnership.Unregister(health, replacement);
            return new JObject
            {
                ["numberCount"] = numbers, ["suppressedCount"] = suppressed, ["positiveOwned"] = owned,
                ["blockedUnclaimed"] = blockedUnclaimed, ["rejectedUnclaimed"] = rejectedUnclaimed,
                ["disabledReleased"] = disabledReleased, ["oldLeaseKeptNew"] = oldLeaseKeptNew,
                ["unregistered"] = !CombatDamageNumberOwnership.OwnsNumber(health, 10f)
            };
        }
        finally
        {
            CombatDamageNumberOwnership.Unregister(health, presenter);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static JObject CombatPhysicsRegionDelivery(JObject args)
    {
        var root = new GameObject("Explicit trigger receiver fixture"); root.SetActive(false);
        try
        {
            var first = root.AddComponent<CombatPhysicsTriggerProbe>();
            var second = root.AddComponent<CombatPhysicsTriggerProbe>();
            var regionRoot = new GameObject("Trigger region"); regionRoot.transform.SetParent(root.transform, false);
            regionRoot.AddComponent<BoxCollider2D>();
            var region = regionRoot.AddComponent<ItemPhysicsRegion2D>();
            region.BindRegion("trigger", WorldPhysicsPurpose2D.Trigger, second);
            region.ForwardTrigger(null, WorldPhysicsTriggerPhase2D.Enter);
            region.ForwardTrigger(null, WorldPhysicsTriggerPhase2D.Stay);
            region.ForwardTrigger(null, WorldPhysicsTriggerPhase2D.Exit);
            region.BindRegion("trigger", WorldPhysicsPurpose2D.Trigger, first);
            region.ForwardTrigger(null, WorldPhysicsTriggerPhase2D.Enter);
            region.BindRegion("trigger", WorldPhysicsPurpose2D.Trigger, null);
            region.ForwardTrigger(null, WorldPhysicsTriggerPhase2D.Enter);
            return new JObject { ["firstCount"] = first.Count, ["secondCount"] = second.Count,
                ["secondLastPhase"] = second.LastPhase.ToString() };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private sealed class CombatPhysicsDamageTargetProbe : IGameplayPhysicsDamageTarget
    {
        public void ReceivePhysicsDamage(Mod_Damage weapon, in CombatDamageContext context) { }
    }

    private sealed class CombatNumberPresenterProbe : ICombatDamageNumberPresenter
    {
        public bool CanPresentDamageNumber { get; set; } = true;
    }
    #endregion
}

internal sealed class CombatPhysicsTriggerProbe : MonoBehaviour, IWorldPhysicsTriggerReceiver2D
{
    #region 原生消息接收采样
    public int Count { get; private set; }
    public WorldPhysicsTriggerPhase2D LastPhase { get; private set; }
    public void ReceivePhysicsTrigger(Collider2D other, WorldPhysicsTriggerPhase2D phase)
    { Count++; LastPhase = phase; }
    #endregion
}
