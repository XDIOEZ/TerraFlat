using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using Unity.Mathematics;
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
        Register("combat", "hit_contact_positions", CombatHitContactPositions);
    }

    private static JObject CombatHitContactPositions(JObject args)
    {
        using var space = WorldPhysicsScene2D.CreateIsolated("Combat contact points " + Guid.NewGuid().ToString("N"));
        using var physics = new WorldPhysicsService2D(space.Scene.name, WorldPhysicsScene2D.Borrow(space.Scene));
        var root = new GameObject("Contact geometry fixture");
        SceneManager.MoveGameObjectToScene(root, space.Scene);
        root.transform.position = new Vector3(30f, 30f);
        var attack = root.AddComponent<BoxCollider2D>();
        attack.isTrigger = true;
        attack.size = new Vector2(.2f, 2f);
        var samples = new JArray();
        try
        {
            foreach (float angle in new[] { 0f, 45f })
            foreach (bool circle in new[] { false, true })
            {
                root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                var targets = new Collider2D[2];
                using var query = physics.RentCombatDamageQuery();
                for (int i = 0; i < targets.Length; i++)
                {
                    var target = new GameObject("Contact target " + i);
                    target.transform.SetParent(root.transform, false);
                    target.transform.localPosition = new Vector3(0f, i == 0 ? .55f : -.55f);
                    float2 center = (float2)(Vector2)target.transform.position;
                    if (circle)
                    {
                        var collider = target.AddComponent<CircleCollider2D>();
                        collider.radius = .7f;
                        targets[i] = collider;
                        query.Add(i, PerceptionShape2D.Circle(center, .7f));
                    }
                    else
                    {
                        var collider = target.AddComponent<BoxCollider2D>();
                        collider.size = new Vector2(1f, 1.3f);
                        targets[i] = collider;
                        query.Add(i, PerceptionShape2D.Aabb(center, new float2(.5f, .65f)), angle);
                    }
                    targets[i].isTrigger = true;
                }
                Physics2D.SyncTransforms();
                var shape = new AttackShape2D { Center = (float2)(Vector2)root.transform.position,
                    HalfExtents = new float2(.1f, 1f), Rotation = angle * Mathf.Deg2Rad };
                foreach (bool initialSweep in new[] { false, true })
                {
                    shape.SweepDelta = initialSweep ? new float2(.01f, 0f) : float2.zero;
                    var hits = new List<CombatColliderHit2D>();
                    query.Query(shape, hits);
                    var points = new JArray();
                    foreach (CombatColliderHit2D hit in hits)
                    {
                        Vector2 direct = CombatColliderQuery2D.CalculateOverlapContactPoint(attack, targets[hit.CandidateId]);
                        points.Add(new JObject { ["target"] = hit.CandidateId,
                            ["localY"] = root.transform.InverseTransformPoint(hit.Point).y,
                            ["directLocalY"] = root.transform.InverseTransformPoint(direct).y,
                            ["targetPointError"] = Vector2.Distance(hit.Point, targets[hit.CandidateId].ClosestPoint(hit.Point)),
                            ["attackPointError"] = Vector2.Distance(hit.Point + (Vector2)shape.SweepDelta,
                                attack.ClosestPoint(hit.Point + (Vector2)shape.SweepDelta)),
                            ["fraction"] = hit.Fraction });
                    }
                    samples.Add(new JObject { ["angle"] = angle, ["circle"] = circle,
                        ["initialSweep"] = initialSweep, ["points"] = points });
                }
                foreach (Collider2D target in targets) UnityEngine.Object.DestroyImmediate(target.gameObject);
            }
            var castHits = new List<CombatColliderHit2D>();
            var rawCastHits = new List<RaycastHit2D>();
            using (var query = physics.RentCombatDamageQuery())
            {
                query.Add(9, PerceptionShape2D.Aabb(new float2(30f, 30f), new float2(.5f)));
                query.Query(new AttackShape2D { Center = new float2(32f, 30f), HalfExtents = new float2(.1f),
                    SweepDelta = new float2(4f, 0f) }, castHits);
                GameplayPhysics2D.BoxCast(space, new Vector2(28f, 30f), new Vector2(.2f, .2f), 0f,
                    Vector2.right, new ContactFilter2D { useTriggers = true, useLayerMask = true,
                        layerMask = CombatPhysicsChannels.DamageReceiverMask }, rawCastHits, 4f);
            }
            using var receiver = new GameplayTestInvulnerabilityFixture();
            receiver.Health.Data.DamageInterval = -1f;
            receiver.Sender.DamageValues = new CombatDamage();
            receiver.Health.transform.position = new Vector3(8f, 9f);
            DamageReceiverDamageInfo info = null;
            receiver.Health.OnHitReceived += hit => info = hit;
            Vector2 contactPoint = new Vector2(7.75f, 9.3f);
            float received = receiver.Health.HurtAtHitPoint(receiver.Sender, contactPoint);
            float receiverError = info == null ? -1f : Vector2.Distance(info.HitPosition, contactPoint);
            float contextError = info == null ? -1f : Vector2.Distance((Vector2)info.Context.HitPoint, contactPoint);
            info = null;
            receiver.Health.Hurt(receiver.Sender);
            float legacyError = info == null ? -1f : Vector2.Distance(info.HitPosition, receiver.Health.transform.position);
            info = null;
            receiver.Health.HurtEnvironmentalDamage(1, Vector2.zero, new CombatDamage());
            float environmentalError = info == null ? -1f : Vector2.Distance(info.HitPosition, receiver.Health.transform.position);
            info = null;
            CombatDamageContext dataContext = receiver.Context(GameplayFixedStepSimulation.CurrentTime);
            dataContext.HitPoint = new float2(100f, 100f);
            receiver.Health.Hurt(dataContext);
            float dataDefaultError = info == null ? -1f : Vector2.Distance(info.HitPosition, receiver.Health.transform.position);
            return new JObject { ["samples"] = samples, ["received"] = received,
                ["receiverError"] = receiverError, ["contextError"] = contextError,
                ["legacyError"] = legacyError, ["environmentalError"] = environmentalError,
                ["dataDefaultError"] = dataDefaultError, ["castCount"] = castHits.Count,
                ["castFraction"] = castHits.Count == 1 ? castHits[0].Fraction : -1f,
                ["castPointError"] = castHits.Count == 1 && rawCastHits.Count == 1
                    ? Vector2.Distance(castHits[0].Point, rawCastHits[0].point) : -1f };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
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
