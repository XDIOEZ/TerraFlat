using System;
using System.Collections.Generic;
using FlatWorld.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 世界物理空间与区域归属

    private static void RegisterWorldPhysicsOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("world_streaming/world_physics_ownership", ServiceWorldPhysicsOwnership);
        operations.Add("world_streaming/world_physics_definition", ServiceWorldPhysicsDefinition);
    }

    // 自有原生场景实测隔离与接触，采样只调用正式登记和解析入口。
    private static JObject ServiceWorldPhysicsOwnership(JObject args)
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var firstSpace = WorldPhysicsScene2D.CreateIsolated("Physics probe A " + suffix);
        using var secondSpace = WorldPhysicsScene2D.CreateIsolated("Physics probe B " + suffix);
        using var first = new WorldPhysicsService2D("probe:physics-a:" + suffix, WorldPhysicsScene2D.Borrow(firstSpace.Scene));
        using var second = new WorldPhysicsService2D("probe:physics-b:" + suffix, WorldPhysicsScene2D.Borrow(secondSpace.Scene));
        bool separateNativeScenes = firstSpace.PhysicsScene != secondSpace.PhysicsScene;
        var roots = new List<GameObject>();
        try
        {
            BoxCollider2D firstMarker = CreateWorldPhysicsProbeBox(firstSpace, "A query marker",
                new Vector2(10f, 10f), Vector2.one, roots);
            BoxCollider2D secondMarker = CreateWorldPhysicsProbeBox(secondSpace, "B query marker",
                new Vector2(10f, 10f), Vector2.one, roots);
            BoxCollider2D otherWorldWall = CreateWorldPhysicsProbeBox(secondSpace, "B wall",
                Vector2.zero, new Vector2(0.5f, 4f), roots);
            BoxCollider2D movingCollider = CreateWorldPhysicsProbeBox(firstSpace, "A moving body",
                new Vector2(-2f, 0f), Vector2.one * 0.5f, roots);
            Rigidbody2D movingBody = movingCollider.gameObject.AddComponent<Rigidbody2D>();
            movingBody.gravityScale = 0f;
            movingBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            movingBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            movingBody.sleepMode = RigidbodySleepMode2D.NeverSleep;
            first.RegisterCollider(firstMarker, regionId: "marker:a");
            second.RegisterCollider(secondMarker, regionId: "marker:b");
            second.RegisterCollider(otherWorldWall, regionId: "wall:b");
            first.RegisterCollider(movingCollider, regionId: "moving:a");

            bool sharedSpaceRejected = false, wrongSceneRejected = false;
            using (var borrowedSpace = WorldPhysicsScene2D.Borrow(firstSpace.Scene))
            {
                try
                {
                    using var invalid = new WorldPhysicsService2D(second.WorldKey, borrowedSpace);
                }
                catch (InvalidOperationException) { sharedSpaceRejected = true; }
            }
            try { first.RegisterCollider(secondMarker, regionId: "wrong-world"); }
            catch (InvalidOperationException) { wrongSceneRejected = true; }
            bool wrongSceneOwnerPreserved = WorldPhysicsService2D.TryResolveCollider(secondMarker, out var preserved) &&
                ReferenceEquals(preserved.World, second) && preserved.RegionId == "marker:b";

            var hits = new Collider2D[8];
            var filter = new ContactFilter2D { useTriggers = true };
            Physics2D.SyncTransforms();
            int firstCount = firstSpace.PhysicsScene.OverlapPoint(new Vector2(10f, 10f), filter, hits);
            bool firstOwnMarker = firstCount == 1 && hits[0] == firstMarker;
            Array.Clear(hits, 0, hits.Length);
            int secondCount = secondSpace.PhysicsScene.OverlapPoint(new Vector2(10f, 10f), filter, hits);
            bool secondOwnMarker = secondCount == 1 && hits[0] == secondMarker;

            movingBody.velocity = Vector2.right * 4f;
            for (int i = 0; i < 50; i++) firstSpace.PhysicsScene.Simulate(0.02f);
            float passedOtherWorldWallX = movingBody.position.x;
            bool otherWorldContact = movingCollider.IsTouching(otherWorldWall);

            BoxCollider2D ownWall = CreateWorldPhysicsProbeBox(firstSpace, "A wall",
                Vector2.zero, new Vector2(0.5f, 4f), roots);
            first.RegisterCollider(ownWall, regionId: "wall:a");
            movingBody.position = new Vector2(-2f, 0f);
            movingBody.velocity = Vector2.right * 4f;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 50; i++) firstSpace.PhysicsScene.Simulate(0.02f);
            float stoppedAtOwnWallX = movingBody.position.x;
            bool ownWorldContact = movingCollider.IsTouching(ownWall);

            JObject ownership = CaptureWorldPhysicsRegionOwnership(first, second, firstSpace, secondSpace, roots);
            JObject combatQueries = CaptureWorldPhysicsCombatQueries(firstSpace, secondSpace);
            first.Dispose();
            bool borrowedSpacePreserved = firstSpace.IsValid && movingBody != null &&
                !firstSpace.PhysicsScene.IsEmpty();
            bool disposedIndexCleared = !WorldPhysicsService2D.TryResolveCollider(firstMarker, out _) &&
                !WorldPhysicsService2D.TryResolveCollider(ownWall, out _) && first.ColliderCount == 0;
            bool otherWorldSurvived = WorldPhysicsService2D.TryResolveCollider(secondMarker, out var surviving) &&
                ReferenceEquals(surviving.World, second);
            return new JObject
            {
                ["separateNativeScenes"] = separateNativeScenes,
                ["firstQueryCount"] = firstCount, ["secondQueryCount"] = secondCount,
                ["firstOwnMarker"] = firstOwnMarker, ["secondOwnMarker"] = secondOwnMarker,
                ["sharedSpaceRejected"] = sharedSpaceRejected, ["wrongSceneRejected"] = wrongSceneRejected,
                ["wrongSceneOwnerPreserved"] = wrongSceneOwnerPreserved,
                ["passedOtherWorldWallX"] = passedOtherWorldWallX, ["otherWorldContact"] = otherWorldContact,
                ["stoppedAtOwnWallX"] = stoppedAtOwnWallX, ["ownWorldContact"] = ownWorldContact,
                ["ownership"] = ownership, ["disposedIndexCleared"] = disposedIndexCleared,
                ["borrowedSpacePreserved"] = borrowedSpacePreserved,
                ["otherWorldSurvived"] = otherWorldSurvived, ["combatQueries"] = combatQueries
            };
        }
        finally
        {
            for (int i = roots.Count - 1; i >= 0; i--)
                if (roots[i] != null) UnityEngine.Object.DestroyImmediate(roots[i]);
        }
    }

    private static JObject CaptureWorldPhysicsCombatQueries(WorldPhysicsScene2D firstSpace, WorldPhysicsScene2D secondSpace)
    {
        Scene previousScene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        Scene temporaryActiveScene = default;
#endif
        var results = new List<CombatColliderHit2D>();
        var attack = new AttackShape2D { Center = new float2(30f, 30f), HalfExtents = new float2(0.2f) };
        using CombatColliderQuery2D first = CombatColliderQuery2D.Rent(firstSpace);
        using CombatColliderQuery2D second = CombatColliderQuery2D.Rent(secondSpace);
        first.Add(101, PerceptionShape2D.Aabb(attack.Center, new float2(0.25f)));
        second.Add(202, PerceptionShape2D.Circle(attack.Center, 0.25f));
        try
        {
            Scene defaultScene = secondSpace.Scene;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                temporaryActiveScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
                defaultScene = temporaryActiveScene;
            }
#endif
            SceneManager.SetActiveScene(defaultScene);
            bool defaultSceneChanged = defaultScene != previousScene && SceneManager.GetActiveScene() == defaultScene;
            first.Query(attack, results);
            bool fixedSpaceHit = results.Count == 1 && results[0].CandidateId == 101;
            second.Query(attack, results);
            bool separateCandidateHit = results.Count == 1 && results[0].CandidateId == 202;
            AttackShape2D sweep = attack;
            sweep.Center += new float2(1f, 0f);
            sweep.SweepDelta = new float2(2f, 0f);
            first.Query(sweep, results);
            bool fixedSpaceSweep = results.Count == 1 && results[0].CandidateId == 101 &&
                results[0].Fraction >= 0f && results[0].Fraction < 1f;
            first.Dispose();
            using CombatColliderQuery2D reused = CombatColliderQuery2D.Rent(firstSpace);
            reused.Add(303, PerceptionShape2D.Circle(new float2(35f, 30f), 0.25f));
            reused.Query(attack, results);
            bool oldCandidateCleared = results.Count == 0;
            AttackShape2D newAttack = attack;
            newAttack.Center = new float2(35f, 30f);
            reused.Query(newAttack, results);
            bool newCandidateHit = results.Count == 1 && results[0].CandidateId == 303;

            using var leasedSpace = WorldPhysicsScene2D.Borrow(firstSpace.Scene);
            using CombatColliderQuery2D invalidated = CombatColliderQuery2D.Rent(leasedSpace);
            invalidated.Add(404, PerceptionShape2D.Aabb(new float2(45f, 30f), new float2(0.25f)));
            leasedSpace.Dispose();
            bool disposedSpaceRejected = false;
            try { invalidated.Query(attack, results); }
            catch (ObjectDisposedException) { disposedSpaceRejected = true; }
            catch (InvalidOperationException) { disposedSpaceRejected = true; }
            var hits = new Collider2D[4];
            int disposedProjectionCount = firstSpace.PhysicsScene.OverlapPoint(new Vector2(45f, 30f),
                new ContactFilter2D { useTriggers = true }, hits);
            return new JObject
            {
                ["defaultSceneChanged"] = defaultSceneChanged, ["fixedSpaceHit"] = fixedSpaceHit,
                ["separateCandidateHit"] = separateCandidateHit, ["fixedSpaceSweep"] = fixedSpaceSweep,
                ["oldCandidateCleared"] = oldCandidateCleared, ["newCandidateHit"] = newCandidateHit,
                ["disposedSpaceRejected"] = disposedSpaceRejected, ["disposedProjectionCount"] = disposedProjectionCount
            };
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
#if UNITY_EDITOR
            if (temporaryActiveScene.IsValid() && temporaryActiveScene.isLoaded)
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(temporaryActiveScene, true);
#endif
        }
    }

    private static JObject ServiceWorldPhysicsDefinition(JObject args)
    {
        using var space = WorldPhysicsScene2D.CreateIsolated("Physics JSON probe " + Guid.NewGuid().ToString("N"));
        using var world = new WorldPhysicsService2D("probe:physics-json:" + Guid.NewGuid().ToString("N"), space);
        var root = new GameObject("JSON multi-region item");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, space.Scene);
        GameItem item = root.AddComponent<GameItem>();
        try
        {
            BoxCollider2D legacyInteraction = root.AddComponent<BoxCollider2D>();
            legacyInteraction.isTrigger = true;
            var solidRoot = new GameObject("Legacy solid");
            solidRoot.transform.SetParent(root.transform, false);
            BoxCollider2D legacySolid = solidRoot.AddComponent<BoxCollider2D>();
            Mod_DamageReceiver firstReceiver = CreateWorldPhysicsProbeReceiver(item, "health:first", new Vector2(-2f, 0));
            Mod_DamageReceiver secondReceiver = CreateWorldPhysicsProbeReceiver(item, "health:second", new Vector2(2f, 0));
            var interactionRoot = new GameObject("Interaction module");
            interactionRoot.transform.SetParent(root.transform, false);
            Mod_InteractReciver interactionReceiver = interactionRoot.AddComponent<Mod_InteractReciver>();
            interactionReceiver.modData = new Ex_ModData();
            interactionReceiver.BindRuntimeIdentity("interact:module", "probe:interaction", "probe:interaction");
            const string json = @"{
                ""id"":""probe:multi-region"",
                ""physics"":{""regions"":[
                    {""id"":""body:left"",""purposes"":""Blocking"",""collider"":{""type"":""BoxCollider2D"",""offset"":{""x"":-0.75,""y"":0},""size"":{""x"":0.4,""y"":1}}},
                    {""id"":""body:right"",""purposes"":""Blocking"",""collider"":{""type"":""BoxCollider2D"",""offset"":{""x"":0.75,""y"":0},""size"":{""x"":0.4,""y"":1}}},
                    {""id"":""interact"",""purposes"":""Interaction, Trigger"",""module"":""interact:module"",""collider"":{""type"":""CircleCollider2D"",""radius"":1.5}},
                    {""id"":""damage:first"",""purposes"":""Damage, Trigger"",""module"":""health:first"",""collider"":{""type"":""BoxCollider2D"",""size"":{""x"":0.5,""y"":0.75}}},
                    {""id"":""damage:second"",""purposes"":""Damage, Trigger"",""module"":""health:second"",""collider"":{""type"":""BoxCollider2D"",""size"":{""x"":0.25,""y"":0.5}}}
                ]}}
            ";
            ItemPhysicsDefinitionDto physics = JsonConvert.DeserializeObject<ItemDefinitionDto>(json).Physics;
            ItemPhysicsDefinitionRuntime.ConfigureItemPhysicsRegions(item, physics);
            ItemPhysicsRegion2D[] regions = item.GetComponentsInChildren<ItemPhysicsRegion2D>(true);
            ItemPhysicsRegion2D left = Array.Find(regions, region => region.RegionId == "body:left");
            ItemPhysicsRegion2D right = Array.Find(regions, region => region.RegionId == "body:right");
            ItemPhysicsRegion2D interaction = Array.Find(regions, region => region.RegionId == "interact");
            ItemPhysicsRegion2D firstDamage = Array.Find(regions, region => region.RegionId == "damage:first");
            ItemPhysicsRegion2D secondDamage = Array.Find(regions, region => region.RegionId == "damage:second");
            var baseline = new PooledItemMarker(item);
            baseline.CaptureBaseline();
            firstDamage.GetComponent<BoxCollider2D>().size = Vector2.one * 9f;
            firstDamage.transform.localPosition = Vector3.one * 9f;
            ItemPhysicsDefinitionRuntime.ConfigureItemPhysicsRegions(item, physics);
            ItemPhysicsRegion2D[] reused = item.GetComponentsInChildren<ItemPhysicsRegion2D>(true);
            bool stableRegions = reused.Length == 5 && regions.Length == 5 &&
                Array.Find(reused, region => region.RegionId == "body:left") == left &&
                Array.Find(reused, region => region.RegionId == "body:right") == right &&
                Array.Find(reused, region => region.RegionId == "damage:first") == firstDamage &&
                Array.Find(reused, region => region.RegionId == "damage:second") == secondDamage;
            bool shapeReset = firstDamage.GetComponent<BoxCollider2D>().size == new Vector2(0.5f, 0.75f) &&
                firstDamage.transform.localPosition == Vector3.zero;
            firstDamage.transform.localPosition = Vector3.one;
            baseline.RestoreBaseline();
            bool baselineStable = baseline.HasOriginalHierarchy() && firstDamage.transform.localPosition == Vector3.zero;
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(firstReceiver.GetComponent<Collider2D>(),
                new ItemColliderDefinitionDto { Enabled = true });
            bool oldShapesDisabled = !legacyInteraction.enabled && !legacySolid.enabled &&
                !firstReceiver.GetComponent<Collider2D>().enabled && !secondReceiver.GetComponent<Collider2D>().enabled;
            root.SetActive(true);
            world.RegisterItem(item);
            bool twoExactReceivers = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(firstDamage.GetComponent<Collider2D>()) == firstReceiver &&
                GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(secondDamage.GetComponent<Collider2D>()) == secondReceiver;
            bool exactInteractionReceiver = ReferenceEquals(GameplayPhysics2D.ResolveComponent<IInteractable>(interaction.GetComponent<Collider2D>()), interactionReceiver);
            bool blockingHasNoDamageReceiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(left.GetComponent<Collider2D>()) == null &&
                GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(interaction.GetComponent<Collider2D>()) == null;
            world.RegisterCollider(legacyInteraction, item, firstReceiver, "interaction:wrong-receiver", WorldPhysicsPurpose2D.Interaction);
            bool purposePrecedesReceiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(legacyInteraction) == null;
            var hits = new Collider2D[8];
            var solidsOnly = new ContactFilter2D { useTriggers = false };
            Physics2D.SyncTransforms();
            int gapSolids = space.PhysicsScene.OverlapPoint(Vector2.zero, solidsOnly, hits);
            int leftSolids = space.PhysicsScene.OverlapPoint(new Vector2(-0.75f, 0), solidsOnly, hits);
            bool leftGeometryPreserved = leftSolids == 1 && hits[0] == left.GetComponent<Collider2D>();
            return new JObject
            {
                ["regionCount"] = regions.Length, ["stableRegions"] = stableRegions,
                ["shapeReset"] = shapeReset, ["baselineStable"] = baselineStable,
                ["oldShapesDisabled"] = oldShapesDisabled, ["twoExactReceivers"] = twoExactReceivers,
                ["exactInteractionReceiver"] = exactInteractionReceiver,
                ["blockingHasNoDamageReceiver"] = blockingHasNoDamageReceiver,
                ["purposePrecedesReceiver"] = purposePrecedesReceiver,
                ["gapSolids"] = gapSolids, ["leftGeometryPreserved"] = leftGeometryPreserved,
                ["interactionIsCircle"] = interaction.GetComponent<CircleCollider2D>() != null &&
                    interaction.GetComponent<CircleCollider2D>().radius == 1.5f
            };
        }
        finally
        {
            WorldPhysicsService2D.ReleaseItem(item);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Mod_DamageReceiver CreateWorldPhysicsProbeReceiver(Item item, string stableName, Vector2 position)
    {
        var root = new GameObject(stableName);
        root.transform.SetParent(item.transform, false);
        root.transform.localPosition = position;
        Mod_DamageReceiver receiver = root.AddComponent<Mod_DamageReceiver>();
        receiver.modData = new Ex_ModData();
        receiver.BindRuntimeIdentity(stableName, "probe:health", "probe:health");
        receiver.GetComponent<Collider2D>().isTrigger = true;
        return receiver;
    }

    private static JObject CaptureWorldPhysicsRegionOwnership(WorldPhysicsService2D first,
        WorldPhysicsService2D second, WorldPhysicsScene2D firstSpace, WorldPhysicsScene2D secondSpace,
        List<GameObject> roots)
    {
        var itemRoot = new GameObject("Multi-region physics item");
        itemRoot.SetActive(false);
        roots.Add(itemRoot);
        SceneManager.MoveGameObjectToScene(itemRoot, firstSpace.Scene);
        var item = itemRoot.AddComponent<GameItem>();
        var firstReceiverRoot = new GameObject("First receiver");
        firstReceiverRoot.transform.SetParent(itemRoot.transform, false);
        var firstReceiver = firstReceiverRoot.AddComponent<Mod_DamageReceiver>();
        var secondReceiverRoot = new GameObject("Second receiver");
        secondReceiverRoot.transform.SetParent(itemRoot.transform, false);
        var secondReceiver = secondReceiverRoot.AddComponent<Mod_DamageReceiver>();
        var regionRoot = new GameObject("Selected damage region");
        regionRoot.transform.SetParent(itemRoot.transform, false);
        var region = regionRoot.AddComponent<BoxCollider2D>();
        region.isTrigger = true;
        object entity = new object();
        first.RegisterCollider(region, item, secondReceiver, "damage:second",
            WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Trigger, entity);
        bool registered = WorldPhysicsService2D.TryResolveCollider(region, out var binding);
        bool originalItem = registered && binding.Item == item;
        bool exactReceiver = registered && binding.Receiver == secondReceiver &&
            GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(region) == secondReceiver;
        string regionId = registered ? binding.RegionId : null;
        bool originalEntity = registered && ReferenceEquals(binding.Entity, entity);
        bool damageOnly = registered && (binding.Purpose & WorldPhysicsPurpose2D.Damage) != 0 &&
            (binding.Purpose & WorldPhysicsPurpose2D.Blocking) == 0;

        BoxCollider2D image = CreateWorldPhysicsProbeBox(firstSpace, "First source image",
            new Vector2(20f, 0f), Vector2.one, roots);
        image.gameObject.AddComponent<ColliderSource2D>().Bind(region, new Vector2(20f, 0f));
        BoxCollider2D secondImage = CreateWorldPhysicsProbeBox(firstSpace, "Second source image",
            new Vector2(40f, 0f), Vector2.one, roots);
        secondImage.gameObject.AddComponent<ColliderSource2D>().Bind(image, new Vector2(20f, 0f));
        bool nestedSource = ColliderSource2D.Resolve(secondImage) == region;
        bool nestedOwnership = WorldPhysicsService2D.TryResolveCollider(secondImage, out var imageBinding) &&
            imageBinding.Item == item && imageBinding.Receiver == secondReceiver && imageBinding.RegionId == regionId;
        ColliderSource2D imageSource = image.GetComponent<ColliderSource2D>();
        imageSource.ClearBinding();
        bool clearedImageReusable = ColliderSource2D.Resolve(image) == image &&
            !WorldPhysicsService2D.TryResolveCollider(secondImage, out _);
        imageSource.Bind(region, new Vector2(20f, 0f));
        bool reboundImageSource = ColliderSource2D.Resolve(secondImage) == region;
        imageSource.Bind(secondImage, Vector2.zero);
        bool sourceCycleRejected = ColliderSource2D.Resolve(image) == null && ColliderSource2D.Resolve(secondImage) == null;
        BoxCollider2D cyclePrefix = CreateWorldPhysicsProbeBox(firstSpace, "Cycle prefix image",
            new Vector2(60f, 0f), Vector2.one, roots);
        cyclePrefix.gameObject.AddComponent<ColliderSource2D>().Bind(secondImage, Vector2.zero);
        bool sourceCyclePrefixRejected = ColliderSource2D.Resolve(cyclePrefix) == null;
        imageSource.Bind(region, new Vector2(20f, 0f));

        first.UnregisterCollider(region);
        bool unregistered = !WorldPhysicsService2D.TryResolveCollider(region, out _) &&
            !WorldPhysicsService2D.TryResolveCollider(secondImage, out _);
        second.AssignPhysicsRootToPrimarySpace(itemRoot);
        second.RegisterCollider(region, item, firstReceiver, "interaction:reused", WorldPhysicsPurpose2D.Interaction);
        bool rebound = WorldPhysicsService2D.TryResolveCollider(region, out var reboundBinding) &&
            ReferenceEquals(reboundBinding.World, second) && reboundBinding.RegionId == "interaction:reused" &&
            reboundBinding.Receiver == firstReceiver && reboundBinding.Purpose == WorldPhysicsPurpose2D.Interaction;
        bool oldWorldImageRejected = !WorldPhysicsService2D.TryResolveCollider(image, out _) &&
            !WorldPhysicsService2D.TryResolveCollider(secondImage, out _);
        second.UnregisterCollider(region);
        UnityEngine.Object.DestroyImmediate(regionRoot);
        bool destroyedSourceRejected = ColliderSource2D.Resolve(secondImage) == null &&
            !WorldPhysicsService2D.TryResolveCollider(secondImage, out _);
        return new JObject
        {
            ["registered"] = registered, ["originalItem"] = originalItem, ["exactReceiver"] = exactReceiver,
            ["regionId"] = regionId, ["originalEntity"] = originalEntity, ["damageOnly"] = damageOnly,
            ["nestedSource"] = nestedSource, ["nestedOwnership"] = nestedOwnership,
            ["clearedImageReusable"] = clearedImageReusable, ["reboundImageSource"] = reboundImageSource,
            ["sourceCycleRejected"] = sourceCycleRejected, ["sourceCyclePrefixRejected"] = sourceCyclePrefixRejected,
            ["unregistered"] = unregistered, ["rebound"] = rebound,
            ["oldWorldImageRejected"] = oldWorldImageRejected,
            ["destroyedSourceRejected"] = destroyedSourceRejected
        };
    }

    private static BoxCollider2D CreateWorldPhysicsProbeBox(WorldPhysicsScene2D space, string name,
        Vector2 position, Vector2 size, List<GameObject> roots)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        roots.Add(root);
        SceneManager.MoveGameObjectToScene(root, space.Scene);
        root.transform.position = position;
        var collider = root.AddComponent<BoxCollider2D>();
        collider.size = size;
        root.SetActive(true);
        return collider;
    }

    #endregion
}
