using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class GameplayTestSystemProbe
{
    #region 水生线路查询去重

    // 夹具只提供逐格可游泳结果，路径采样与环绕仍调用正式 AquaticHabitat。
    private static JObject EntityAquaticTraversal(JObject args)
    {
        Vector2 origin = new((float)Number(args, "originX", 0.1d), (float)Number(args, "originY", 0.1d));
        Vector2 target = new((float)Number(args, "targetX", 2.9d), (float)Number(args, "targetY", 0.1d));
        WorldTopologyDomain topology = Flag(args, "wrapped", false)
            ? new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true) : default;
        var blocked = new HashSet<Vector2Int>();
        if (args["blockedCells"] is JArray cells)
            foreach (JObject cell in cells)
                blocked.Add(new Vector2Int((int)Number(cell, "x", 0), (int)Number(cell, "y", 0)));
        var visits = new JArray();
        bool CanSwim(Vector2 position)
        {
            Vector2Int cell = Vector2Int.FloorToInt(position);
            visits.Add(new JObject { ["x"] = cell.x, ["y"] = cell.y });
            return !blocked.Contains(cell);
        }
        bool preventCornerCutting = Flag(args, "preventCornerCutting", false);
        bool accepted = AquaticHabitat.CanTraverse(origin, target, topology, CanSwim, preventCornerCutting);
        int queries = visits.Count;
        var firstVisits = (JArray)visits.DeepClone();
        float2 normalizedTarget = topology.Normalize(new float2(target.x, target.y));
        blocked.Add(Vector2Int.FloorToInt(new Vector2(normalizedTarget.x, normalizedTarget.y)));
        visits.Clear();
        bool acceptedAfterChange = AquaticHabitat.CanTraverse(origin, target, topology, CanSwim, preventCornerCutting);
        return new JObject
        {
            ["accepted"] = accepted, ["queries"] = queries, ["visits"] = firstVisits,
            ["acceptedAfterChange"] = acceptedAfterChange, ["queriesAfterChange"] = visits.Count
        };
    }

    #endregion
}

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 水陆栖息现场
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterAquaticHabitatRuntimeProbe()
    {
        GameplayTestRuntimeProbe.Register("ai", "aquatic_habitat", CaptureAquaticHabitatRuntime);
        GameplayTestRuntimeProbe.RegisterAsync("ai", "aquatic_combat", CaptureAquaticCombatRuntime);
        GameplayTestRuntimeProbe.Register("ai", "flight_land_predation", CaptureFlightLandPredationRuntime);
        GameplayTestRuntimeProbe.Register("ai", "flight_light_avoidance", CaptureFlightLightAvoidanceRuntime);
    }

    // 使用真实生物和液体层，结束时还原本轮自有地块。
    private static JObject CaptureAquaticHabitatRuntime(Item player, JObject args)
    {
        RuntimeTerrainTileSample dry = FindOpenCell(player, true);
        Vector2 origin = (Vector2)dry.WorldCell + Vector2.one * 0.5f;
        Vector2 waterPosition = WorldTopologyRuntime.NormalizePosition(origin + Vector2.right);
        if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(waterPosition, out RuntimeTerrainTileSample water))
            throw new InvalidOperationException("两栖采样缺少相邻真实地块。");
        string originalId = water.Terrain.GetLiquidId(water.LocalCell.x, water.LocalCell.y);
        int originalLevel = water.Terrain.GetLiquidLevel(water.LocalCell.x, water.LocalCell.y);
        using var frog = new ActorFixture(origin, "Frog");
        using var fish = new ActorFixture(origin + Vector2.right * 2f, "SmallFish");
        Mod_AquaticMovement frogMovement = frog.Actor.itemMods.RequireSingleModById<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId);
        Mod_AquaticMovement fishMovement = fish.Actor.itemMods.RequireSingleModById<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId);
        Mod_DamageReceiver frogHealth = frog.Actor.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        Mod_DamageReceiver fishHealth = fish.Actor.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        float frogHp = frogHealth.Hp, fishHp = fishHealth.Hp;
        frogMovement.TickAquaticEnvironmentAndThreats(20f, false);
        fishMovement.TickAquaticEnvironmentAndThreats(20f, false);
        bool frogDrySafe = !frogMovement.IsStranded && frogHealth.Hp == frogHp;
        bool fishDryDamage = fishMovement.IsStranded && fishHealth.Hp < fishHp;
        try
        {
            if (!WorldLiquidSystem.TrySet(water, "core:dirty_water", 100))
                throw new InvalidOperationException("两栖采样无法写入测试水格。");
            bool waterKnown = AquaticHabitat.TryGetDepth(waterPosition, out float waterDepth);
            bool shoreline = AquaticHabitat.CanTraverse(origin, waterPosition, true, false);
            bool fishCannotCrossLand = !AquaticHabitat.CanTraverse(origin, waterPosition, false, true);
            bool fishWater = AquaticHabitat.CanTraverse(waterPosition, waterPosition, false, true);
            if (!WorldLiquidSystem.TrySet(water, "core:lava", 100))
                throw new InvalidOperationException("两栖采样无法写入测试岩浆格。");
            bool lavaKnown = AquaticHabitat.TryGetDepth(waterPosition, out float lavaDepth);
            return new JObject
            {
                ["frogDrySafe"] = frogDrySafe, ["fishDryDamage"] = fishDryDamage,
                ["waterKnown"] = waterKnown, ["waterDepth"] = waterDepth,
                ["shoreline"] = shoreline, ["fishCannotCrossLand"] = fishCannotCrossLand, ["fishWater"] = fishWater,
                ["lavaKnown"] = lavaKnown, ["lavaDepth"] = lavaDepth,
                ["lavaRejected"] = !AquaticHabitat.CanTraverse(origin, waterPosition, true, false) &&
                    !AquaticHabitat.CanTraverse(waterPosition, waterPosition, false, true)
            };
        }
        finally
        {
            if (water.Terrain.GetLiquidLevel(water.LocalCell.x, water.LocalCell.y) != originalLevel ||
                (originalLevel > 0 && water.Terrain.GetLiquidId(water.LocalCell.x, water.LocalCell.y) != originalId))
                if (!WorldLiquidSystem.TrySet(water, originalId, originalLevel))
                    throw new InvalidOperationException("两栖采样未能还原液体格。");
        }
    }

    // 使用真实水格和战斗能力，跨固定步观察游动及攻击窗口撤销。
    private static IEnumerator CaptureAquaticCombatRuntime(Item player, JObject args, Action<JObject> completed)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player, true);
        Vector2 origin = (Vector2)site.WorldCell + Vector2.one * 0.5f;
        var positions = new Vector2[4];
        var cells = new RuntimeTerrainTileSample[4];
        var ids = new string[4];
        var levels = new int[4];
        for (int i = 0; i < cells.Length; i++)
        {
            positions[i] = WorldTopologyRuntime.NormalizePosition(origin + Vector2.right * i);
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(positions[i], out cells[i]))
                throw new InvalidOperationException("水生战斗缺少连续测试地块。");
            ids[i] = cells[i].Terrain.GetLiquidId(cells[i].LocalCell.x, cells[i].LocalCell.y);
            levels[i] = cells[i].Terrain.GetLiquidLevel(cells[i].LocalCell.x, cells[i].LocalCell.y);
        }
        ActorFixture shark = null, prey = null;
        try
        {
            if (!WorldLiquidSystem.TrySet(cells[0], "core:dirty_water", 100) ||
                !WorldLiquidSystem.TrySet(cells[2], "core:dirty_water", 100) ||
                !WorldLiquidSystem.TrySet(cells[3], "core:dirty_water", 100))
                throw new InvalidOperationException("水生战斗无法设置测试水格。");
            shark = new ActorFixture(positions[0], "Shark");
            prey = new ActorFixture(positions[2], "SmallFish");
            Mod_AnimalBehaviorTree sharkTree = shark.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI);
            Mod_AnimalBehaviorTree preyTree = prey.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI);
            Mod_DamageReceiver preyHealth = prey.Actor.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
            preyHealth.Data.Hp = preyHealth.Data.MaxHp = 1000f;
            sharkTree.SetAutonomousControlSuspended(true);
            preyTree.SetAutonomousControlSuspended(true);
            Mod_AquaticMovement movement = shark.Actor.itemMods.RequireSingleModById<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId);
            bool shoreBlocked = !movement.CanPursueTargetInWater(prey.Actor);
            if (!WorldLiquidSystem.TrySet(cells[1], "core:dirty_water", 100))
                throw new InvalidOperationException("水生战斗无法接通水路。");
            bool connected = movement.CanPursueTargetInWater(prey.Actor);
            float beforeDistance = WorldTopologyRuntime.Distance(shark.Actor.transform.position, prey.Actor.transform.position);
            bool moveRequested = movement.TrySwimTowardCombatTarget(prey.Actor.transform.position, .25f, 1.4f);
            yield return new WaitForFixedUpdate();
            yield return null;
            float afterDistance = WorldTopologyRuntime.Distance(shark.Actor.transform.position, prey.Actor.transform.position);
            sharkTree.SetAutonomousControlSuspended(false);
            sharkTree.Context.Detector.CurrentItemsInArea = new List<Item> { prey.Actor };
            sharkTree.ModUpdate(.02f);
            bool hasCombat = sharkTree.Context.TryGetCapability("combat", out AnimalCombatCapability combat);
            bool attackStarted = hasCombat && combat.IsAttackLocked;
            bool restrictionActive = movement.RestrictsOutgoingDamageTargets;
            Vector2 lockedPosition = shark.Actor.transform.position;
            sharkTree.ModUpdate(.02f);
            bool lockStoppedMovement = hasCombat && combat.IsAttackLocked &&
                WorldTopologyRuntime.SqrDistance(lockedPosition, shark.Actor.transform.position) < .000001f;
            sharkTree.ModUpdate(.31f);
            Mod_Damage damage = shark.Actor.itemMods.GetMod_ByName("damage") as Mod_Damage;
            if (damage == null) throw new InvalidOperationException("水生战斗缺少真实攻击模块。");
            bool waterTargetAccepted, shoreTargetRejected, externalRejected;
            using (var other = new ActorFixture(positions[3], "Frog"))
            {
                other.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).SetAutonomousControlSuspended(true);
                Mod_DamageReceiver otherHealth = other.Actor.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
                Collider2D otherCollider = WorldPhysicsService2D.GetItemCollider<Collider2D>(otherHealth);
                waterTargetAccepted = damage.CanHitColliderTarget(otherCollider);
                if (!WorldLiquidSystem.TrySet(cells[3], null, 0))
                    throw new InvalidOperationException("水生战斗无法构造岸上目标。");
                int remaining = damage.RemainingAttackTargets;
                float otherHp = otherHealth.Hp;
                damage.ProcessExplicitColliderHit(otherCollider);
                shoreTargetRejected = otherHealth.Hp == otherHp && damage.RemainingAttackTargets == remaining &&
                    !damage.CanHitColliderTarget(otherCollider);
                externalRejected = !damage.TryReserveExternalTarget(GameplayCombatBridge.Identity(other.Actor)) &&
                    damage.RemainingAttackTargets == remaining;
            }
            if (!WorldLiquidSystem.TrySet(cells[3], "core:dirty_water", 100))
                throw new InvalidOperationException("水生战斗无法恢复测试水路。");
            Rigidbody2D preyBody = WorldPhysicsService2D.GetItemRigidbody(prey.Actor);
            Vector2 far = WorldLocalPresentation.ProjectPosition(positions[3]);
            preyBody.position = far;
            prey.Actor.transform.position = new Vector3(far.x, far.y, prey.Actor.transform.position.z);
            ItemMgr.Instance.NotifyRuntimeItemMoved(prey.Actor);
            sharkTree.ModUpdate(.02f);
            bool attackStoppedAtRange = hasCombat && !combat.IsAttackLocked;
            sharkTree.SetAutonomousControlSuspended(true);
            bool restrictionCleared = !movement.RestrictsOutgoingDamageTargets && hasCombat && !combat.IsAttackLocked;
            completed(new JObject
            {
                ["shoreBlocked"] = shoreBlocked, ["connected"] = connected,
                ["moveRequested"] = moveRequested, ["beforeDistance"] = beforeDistance, ["afterDistance"] = afterDistance,
                ["attackStarted"] = attackStarted, ["lockStoppedMovement"] = lockStoppedMovement,
                ["restrictionActive"] = restrictionActive, ["restrictionCleared"] = restrictionCleared,
                ["attackStoppedAtRange"] = attackStoppedAtRange, ["waterTargetAccepted"] = waterTargetAccepted,
                ["shoreTargetRejected"] = shoreTargetRejected, ["externalRejected"] = externalRejected
            });
        }
        finally
        {
            prey?.Dispose();
            shark?.Dispose();
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Terrain.GetLiquidLevel(cells[i].LocalCell.x, cells[i].LocalCell.y) != levels[i] ||
                    (levels[i] > 0 && cells[i].Terrain.GetLiquidId(cells[i].LocalCell.x, cells[i].LocalCell.y) != ids[i]))
                    if (!WorldLiquidSystem.TrySet(cells[i], ids[i], levels[i]))
                        throw new InvalidOperationException("水生战斗未能还原液体格。");
        }
    }

    // 捕食真实陆生猎物并复用伤害结算，取消后不得残留旧目标。
    private static JObject CaptureFlightLandPredationRuntime(Item player, JObject args)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player);
        Vector2 origin = (Vector2)site.WorldCell + Vector2.one * 0.5f;
        string originalId = site.Terrain.GetLiquidId(site.LocalCell.x, site.LocalCell.y);
        int originalLevel = site.Terrain.GetLiquidLevel(site.LocalCell.x, site.LocalCell.y);
        using var eagle = new ActorFixture(origin, "Eagle");
        using var frog = new ActorFixture(origin, "Frog");
        Mod_AnimalFlightMovement flight = eagle.Actor.itemMods.RequireSingleModById<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId);
        eagle.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).SetAutonomousControlSuspended(true);
        frog.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).SetAutonomousControlSuspended(true);
        Mod_ItemDetector detector = eagle.Actor.itemMods.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
        flight.fleeTriggerDistance = flight.fleeSafeDistance = .25f;
        flight.landPreyIds = Array.Empty<string>();
        long request = detector.RequestDetectorUpdate();
        detector.ApplyDetectorResults(request, new List<Item> { frog.Actor });
        flight.TickFlightEnvironmentAndTimers(.5f);
        bool ordinaryThreatDetected = flight.IsEscaping;
        flight.TickEscapeAction(10f);
        flight.StopFlightAction();
        flight.landPreyIds = new[] { "Frog" };
        request = detector.RequestDetectorUpdate();
        detector.ApplyDetectorResults(request, new List<Item> { frog.Actor });
        flight.TickFlightEnvironmentAndTimers(.5f);
        bool preyDoesNotScare = !flight.IsEscaping;
        Nutrition nutrition = eagle.Actor.itemMods.RequireSingleModById<Mod_Food>(ModText.Food).Data.nutrition;
        nutrition.Carbohydrates = nutrition.Fat = 0f;
        flight.landPreyIds = new[] { "Frog" };
        flight.aquaticPreyTag = "__test_no_fish__";
        flight.fishHuntRadius = 2f;
        flight.fishHuntHungerRate = 1f;
        flight.fishDiveRange = 1f;
        flight.fishDiveSeconds = .1f;
        flight.fishDiveDamage = 1f;
        Mod_DamageReceiver health = frog.Actor.itemMods.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        health.Data.DamageInterval = 0f;
        float hp = health.Hp;
        bool acquired = flight.CanStartAquaticHunt && flight.TryAcquireAquaticHuntTarget();
        flight.BeginAquaticHuntDive();
        AnimalBehaviorTreeStatus first = flight.TickAquaticHuntDive(.1f);
        bool wounded = health.Hp < hp && health.Hp > 0f;
        flight.fishDiveDamage = 1000f;
        AnimalBehaviorTreeStatus second = flight.TickAquaticHuntDive(.1f);
        bool killed = health.Hp <= 0f;
        flight.CancelAquaticHunt();
        bool cancelled = flight.TickAquaticHuntDive(.1f) == AnimalBehaviorTreeStatus.Failure;
        try
        {
            if (!WorldLiquidSystem.TrySet(site, "core:dirty_water", 100))
                throw new InvalidOperationException("飞行捕食无法设置测试水格。");
            using var shark = new ActorFixture(origin, "Shark");
            shark.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).SetAutonomousControlSuspended(true);
            flight.landPreyIds = Array.Empty<string>();
            flight.aquaticPreyTag = "Shark";
            flight.TickFlightEnvironmentAndTimers(1f, false);
            float sharkWeight = shark.Actor.itemData.Stack.CurrentWeight;
            bool heavyRejected = !flight.ThreatensAquaticPrey(shark.Actor) && !flight.CanStartAquaticHunt;
            shark.Actor.itemData.Stack.Weight = .5f;
            flight.TickFlightEnvironmentAndTimers(1f, false);
            bool lightAccepted = flight.ThreatensAquaticPrey(shark.Actor) && flight.CanStartAquaticHunt;
            flight.CancelAquaticHunt();
            return new JObject
            {
                ["acquired"] = acquired, ["firstRunning"] = first == AnimalBehaviorTreeStatus.Running,
                ["wounded"] = wounded, ["killCompleted"] = second == AnimalBehaviorTreeStatus.Success && killed,
                ["cancelled"] = cancelled, ["ordinaryThreatDetected"] = ordinaryThreatDetected,
                ["preyDoesNotScare"] = preyDoesNotScare,
                ["sharkWeight"] = sharkWeight,
                ["heavyRejected"] = heavyRejected, ["lightAccepted"] = lightAccepted
            };
        }
        finally
        {
            if (site.Terrain.GetLiquidLevel(site.LocalCell.x, site.LocalCell.y) != originalLevel ||
                (originalLevel > 0 && site.Terrain.GetLiquidId(site.LocalCell.x, site.LocalCell.y) != originalId))
                if (!WorldLiquidSystem.TrySet(site, originalId, originalLevel))
                    throw new InvalidOperationException("飞行捕食未能还原液体格。");
        }
    }

    // 临时隔离太阳光并使用正式点光源，观察飞行者实际选点和主动位移。
    private static JObject CaptureFlightLightAvoidanceRuntime(Item player, JObject args)
    {
        RuntimeTerrainTileSample site = FindOpenCell(player, true);
        Vector2 origin = (Vector2)site.WorldCell + Vector2.one * 0.5f;
        using var bat = new ActorFixture(origin, "Bat");
        bat.Actor.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI).SetAutonomousControlSuspended(true);
        Mod_AnimalFlightMovement flight = bat.Actor.itemMods.RequireSingleModById<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId);
        LightLayerMgr lights = LightLayerMgr.Instance;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo lightValue = typeof(LightLayerMgr).GetField("_timeLightValue", flags);
        FieldInfo hasLight = typeof(LightLayerMgr).GetField("_hasTimeLightValue", flags);
        if (lights == null || lightValue == null || hasLight == null)
            throw new InvalidOperationException("飞行避光缺少世界光照服务。");
        object oldValue = lightValue.GetValue(lights), oldHasLight = hasLight.GetValue(lights);
        GameObject lamp = null;
        try
        {
            lightValue.SetValue(lights, 0f);
            hasLight.SetValue(lights, true);
            if (!lights.TryGetLightLevel(origin, out float background) || background >= .95f)
                throw new InvalidOperationException("飞行避光现场被其他光源完全照亮，无法构造对照。");
            flight.avoidLight = true;
            flight.lightAvoidanceThreshold = Mathf.Max(.35f, background + .02f);
            flight.lightAvoidanceSearchRadius = 3f;
            bool initiallySafe = !flight.IsCurrentFlightPositionTooBright;
            lamp = new GameObject("[FlightLightAvoidanceProbe]");
            lamp.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lamp, bat.Actor.gameObject.scene);
            lamp.transform.position = WorldLocalPresentation.ProjectPosition(origin);
            Light2D source = lamp.AddComponent<Light2D>();
            source.lightType = Light2D.LightType.Point;
            source.color = Color.white;
            source.intensity = 1f;
            source.pointLightInnerRadius = .25f;
            source.pointLightOuterRadius = 1.25f;
            Light2DSortingLayerUtility.SetLightLayers(source, Array.Empty<int>());
            lamp.SetActive(true);
            bool bright = flight.IsCurrentFlightPositionTooBright;
            bool selected = flight.CanSeekDarkerFlightPosition;
            flight.BeginFlightLightAvoidance();
            Vector2 before = bat.Actor.transform.position;
            bool advanced = false, escaped = false;
            for (int i = 0; i < 40; i++)
            {
                if (!flight.IsCurrentFlightPositionTooBright) { escaped = true; break; }
                if (!flight.TickFlightLightAvoidance(.1f)) break;
                advanced |= WorldTopologyRuntime.SqrDistance(before, bat.Actor.transform.position) > .01f;
            }
            flight.EndFlightLightAvoidance();
            return new JObject
            {
                ["initiallySafe"] = initiallySafe, ["bright"] = bright, ["selected"] = selected,
                ["advanced"] = advanced, ["escaped"] = escaped
            };
        }
        finally
        {
            if (lamp != null) UnityEngine.Object.DestroyImmediate(lamp);
            lightValue.SetValue(lights, oldValue);
            hasLight.SetValue(lights, oldHasLight);
        }
    }
    #endregion
}
