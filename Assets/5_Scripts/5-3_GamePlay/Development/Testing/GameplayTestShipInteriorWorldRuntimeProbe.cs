#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.NaturalEntities;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using ChunkRuntime = FlatWorld.WorldModel.ChunkRuntime;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>只用正式船体、世界、物品、机械和交互入口采样船内世界。</summary>
public static class GameplayTestShipInteriorWorldRuntimeProbe
{
    #region 船内独立世界正式运行时探针
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "space", "ship_interior_world", CaptureShipInteriorWorld);

    private static IEnumerator CaptureShipInteriorWorld(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        if (FlatWorld.Networking.GameNetwork.IsOnline || !FlatWorld.Networking.GameNetwork.HasStateAuthority ||
            Time.timeScale <= 0f)
            throw new InvalidOperationException("船内运行时采样需要隔离单机世界权威和正常时钟。");

        SpaceSession session = SpaceSession.Current ?? SpaceSession.EnsureLoaded();
        var fixture = new ShipInteriorFixture(session, player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        if (!AtmosphereService.TryGetForWorld(GameplayWorldRegistry.GetItemWorldKey(player), out AtmosphereState surfaceAir))
            throw new InvalidOperationException("船内环境采样缺少起始地表大气。");
        double surfacePressure = AtmosphereService.PressureKPa(surfaceAir);
        var surfacePresentation = new GameObject("船内测试的外部世界显示登记");
        SceneManager.MoveGameObjectToScene(surfacePresentation, player.gameObject.scene);
        SpriteRenderer surfaceRenderer = surfacePresentation.AddComponent<SpriteRenderer>();
        GameplayWorldRegistry.RegisterPresentation(surfacePresentation);
        fixture.PhysicsObjects.Add(surfacePresentation);

        Vector2 firstOrigin = OffsetFromPlayer(player, session, 12f);
        Vector2 secondOrigin = OffsetFromPlayer(player, session, 48f);
        ShipState firstShip = AddFirstFloor(fixture, "ShipFloor_Iron", firstOrigin);
        ShipState secondShip = AddFirstFloor(fixture, "ShipFloor_Iron", secondOrigin);
        // 正式结构服务先固定首块地板的身份，让右侧断片实际迁入新船世界。
        ShipStructureService.RebuildAndSplit(firstShip);
        string originalIdentityAnchor = firstShip.IdentityAnchorPieceId;
        AddFloorLine(fixture, firstShip, 1, 8);
        AddFloorLine(fixture, secondShip, 1, 8);
        AddShipPiece(fixture, "ShipFloor_Iron", new Vector2(7.5f, 1.5f), firstShip);
        AddShipPiece(fixture, "ShipFloor_Iron", new Vector2(8.5f, -.5f), firstShip);
        AddShipPiece(fixture, "ShipConsole", new Vector2(7.5f, 1.5f), firstShip);
        AddShipPiece(fixture, "ShipChemicalEngine", new Vector2(8.5f, -.5f), firstShip);

        ShipPieceState firstDoorPiece = AddShipPiece(fixture, "ShipDoor", new Vector2(.5f, .5f), firstShip);
        ShipPieceState secondDoorPiece = AddShipPiece(fixture, "ShipDoor", new Vector2(.5f, .5f), secondShip);
        ShipPieceState rightDoorPiece = AddShipPiece(fixture, "ShipDoor", new Vector2(8.5f, .5f), firstShip);
        AddElectricalAndFluidGrid(fixture, firstShip, "first");
        AddElectricalAndFluidGrid(fixture, secondShip, "second", includeGravityGenerator: false);

        ShipInteriorWorldRuntime firstWorld = session.EnsureShipInteriorWorld(firstShip);
        ShipInteriorWorldRuntime secondWorld = session.EnsureShipInteriorWorld(secondShip);
        Item firstDoor = RequireInteriorItem(session, firstDoorPiece);
        Item secondDoor = RequireInteriorItem(session, secondDoorPiece);
        Item rightDoor = RequireInteriorItem(session, rightDoorPiece);
        Mod_Door firstDoorModule = firstDoor.GetComponentInChildren<Mod_Door>(true)
            ?? throw new InvalidOperationException("首艘船的普通舱门缺少门模块。");
        Mod_Door secondDoorModule = secondDoor.GetComponentInChildren<Mod_Door>(true)
            ?? throw new InvalidOperationException("第二艘船的普通舱门缺少门模块。");
        firstDoorModule.enabled = false;
        firstDoorModule.enabled = true;
        DayTimeSystem time = DayTimeSystem.GetInstance();
        bool clocksReady = time.TryGetResolvedTimeData(firstWorld.WorldKey, out _, out TimeData firstClock) &&
            time.TryGetResolvedTimeData(secondWorld.WorldKey, out _, out TimeData secondClock) &&
            !ReferenceEquals(firstClock, secondClock);
        double timeBefore = firstClock?.GetTotalGameTime() ?? 0d;
        yield return null;
        var doorSorting = firstDoorModule.DoorRenderer.GetComponent<UnityEngine.Rendering.SortingGroup>();

        JObject result = new()
        {
            ["scope"] = "live-isolated-world",
            ["shipCount"] = 2,
            ["ships"] = new JArray(
                ShipWorldSnapshot(session, firstShip, firstWorld, firstDoor),
                ShipWorldSnapshot(session, secondShip, secondWorld, secondDoor))
        };
        result["worldsDistinct"] = firstWorld.WorldKey != secondWorld.WorldKey &&
            !ReferenceEquals(firstWorld.WorldRuntime, secondWorld.WorldRuntime);
        result["localWorldClocksAdvance"] = clocksReady && firstClock.GetTotalGameTime() > timeBefore;
        result["localWorldHasNoPlanetaryWeather"] = WeatherMgr.GetForWorld(firstWorld.WorldKey) == null &&
            WeatherMgr.GetForWorld(secondWorld.WorldKey) == null;
        result["scenesDistinct"] = firstWorld.Scene.handle != secondWorld.Scene.handle;
        result["physicsScenesDistinct"] = !firstWorld.Physics.PrimarySpace.PhysicsScene.Equals(
            secondWorld.Physics.PrimarySpace.PhysicsScene);
        result["sameLocalCoordinates"] = firstDoor.transform.position == secondDoor.transform.position &&
            firstDoor.transform.position == new Vector3(.5f, .5f, 0f);
        result["unlandedAssembliesRetained"] = ReferenceEquals(session.GetShip(firstShip.ShipId), firstShip) &&
            ReferenceEquals(session.GetShip(secondShip.ShipId), secondShip) &&
            session.GetFlight(firstShip.ShipId)?.LandingResolved == false &&
            session.GetFlight(secondShip.ShipId)?.LandingResolved == false;
        result["doorVisualSurvivedImmediateReenable"] = doorSorting != null && doorSorting.enabled &&
            firstDoorModule.isActiveAndEnabled && !firstDoorModule.DoorRenderer.enabled;
        bool doorProjectionFound = false;
        if (session.ShipViewCacheSnapshot()["itemProjections"] is JArray doorProjections)
            foreach (JObject projection in doorProjections)
                if (projection.Value<int>("sourceId") == firstDoor.gameObject.GetInstanceID() &&
                    projection.Value<string>("worldKey") == firstWorld.WorldKey && projection.Value<int>("instanceId") != 0)
                { doorProjectionFound = true; break; }
        result["doorHasWorldPresentationProjection"] = doorProjectionFound;

        var firstItems = new List<Item>();
        var secondItems = new List<Item>();
        GameplayWorldRegistry.CopyWorldItems(firstWorld.WorldKey, firstItems);
        GameplayWorldRegistry.CopyWorldItems(secondWorld.WorldKey, secondItems);
        result["queryOnlyOwnItem"] = firstItems.Contains(firstDoor) && firstItems.Contains(rightDoor) &&
            !firstItems.Contains(secondDoor) && secondItems.Contains(secondDoor) && !secondItems.Contains(firstDoor);

        result["floorProvidesOrdinarySurface"] = ReadShipFloorSupport(firstWorld);
        result["floorVisualNotDuplicated"] = ReadExternalVisualSuppression(firstWorld);
        result["ordinaryGroundPlacementInfersFloor"] = ReadOrdinaryGroundPlacementInference();
        JObject wallResult = PlaceAndRemoveOrdinaryShipWall(fixture, firstShip, firstWorld, player);
        result["ordinaryTileWallMirrored"] = wallResult.Value<bool>("ordinaryTileWallMirrored");
        result["ordinaryTileRemovalUpdatedStructure"] = wallResult.Value<bool>("ordinaryTileRemovalUpdatedStructure");
        result["tileWallBackend"] = wallResult;

        JObject physics = CreateAndSimulateNativeShipPhysics(fixture, firstWorld, secondWorld);
        result["firstShipCollided"] = physics.Value<bool>("firstShipCollided");
        result["secondShipPassed"] = physics.Value<bool>("secondShipPassed");
        result["nativeQueryIsolated"] = physics.Value<bool>("nativeQueryIsolated");
        result["nativePhysics"] = physics;

        Vector2 localProjection = new(4.5f, .5f);
        Vector2 projected = session.ProjectShipInteriorPosition(firstShip, localProjection);
        Vector2 unprojected = session.UnprojectShipInteriorPosition(firstShip, projected);
        result["projectionRoundTrip"] = Vector2.Distance(localProjection, unprojected) < .001f;
        result["projectionSeparatesLocalFromDisplay"] = Vector2.Distance(localProjection, projected) > 1f &&
            Vector2.Distance(session.ProjectShipInteriorPosition(secondShip, localProjection), projected) > 1f;

        MachineFixture machines = fixture.MachineSnapshots[firstShip.ShipId];
        MachineFixture secondMachines = fixture.MachineSnapshots[secondShip.ShipId];
        MachineWorld.TickShip(firstShip.ShipId, .02f);
        MachineWorld.TickShip(secondShip.ShipId, .02f);
        result["machineWorldKey"] = machines.Cultivator.ScopeKey;
        result["machineInstancePreserved"] = MachineStillOwned(firstWorld.WorldKey, machines.Cultivator) &&
            MachineStillOwned(secondWorld.WorldKey, secondMachines.Cultivator) &&
            !ReferenceEquals(machines.Cultivator, secondMachines.Cultivator);
        result["machinePayloadPreserved"] = machines.TankPayload > 0m && secondMachines.TankPayload > 0m &&
            machines.TankPayload != secondMachines.TankPayload &&
            TankPayloadMatches(firstWorld.WorldKey, machines.Tank, machines.TankPayload) &&
            TankPayloadMatches(secondWorld.WorldKey, secondMachines.Tank, secondMachines.TankPayload);
        result["fluidPipeWorldKey"] = machines.Pipe.ScopeKey;
        result["electricalWireWorldKey"] = machines.Wire.ScopeKey;
        result["electricalGridContainsRealNodes"] = ElectricalGridContains(firstWorld.WorldKey,
            machines.Wire, machines.PowerSource, machines.GravityGenerator);
        result["machineVisualLayerMatchesInteraction"] = MachineVisualLayerMatches(machines.Cultivator);

        ShipFlightState firstFlight = session.GetFlight(firstShip.ShipId);
        result["walkingGravityAvailable"] = session.HasArtificialGravity(firstShip, .5d, .5d) ||
            firstFlight?.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating or ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;

        SpacePassengerState passenger = session.GetPassenger(player);
        fixture.Passenger = passenger;
        passenger.LocalX = 1.5d;
        passenger.LocalY = .5d;
        passenger.ShipId = firstShip.ShipId;
        passenger.Supported = true;
        bool entered = session.TryTransferPassengerToShipInteriorWorld(player, passenger, firstShip);
        Mod_Mover mover = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        Mod_GameController controller = player.itemMods?.GetMod_ByID<Mod_GameController>(ModText.Controller);
        result["passengerOwnedLocalPhysics"] = entered && mover?.rb != null &&
            player.gameObject.scene == firstWorld.Scene && mover.rb.gameObject.scene == firstWorld.Scene &&
            GameplayWorldRegistry.GetItemWorldKey(player) == firstWorld.WorldKey && mover.rb.simulated;
        result["passengerChildrenMoved"] = RegisteredPlayerChildrenUseWorld(player, firstWorld.WorldKey);
        using (GameplayWorldRegistry.UseWorld(firstWorld.WorldKey))
        {
            double outsidePressure = session.GetOutside(firstShip).PressureKPa;
            bool sampled = session.TrySample(player, out ItemEnvironmentSample environment);
            result["openShipUsesSurfaceAtmosphere"] = sampled && surfacePressure > 0d &&
                Math.Abs(outsidePressure - surfacePressure) < .001d &&
                Math.Abs(environment.PressureKPa - surfacePressure) < .001d &&
                GameplayWorldRegistry.ExecutionWorldKey == firstWorld.WorldKey;
            result["openShipAtmosphere"] = new JObject { ["surfacePressureKPa"] = surfacePressure,
                ["outsidePressureKPa"] = outsidePressure, ["playerPressureKPa"] = environment.PressureKPa,
                ["temperatureCelsius"] = environment.TemperatureCelsius };
        }

        bool movementDelegated = false;
        if (entered && mover?.rb != null && controller != null)
        {
            movementDelegated = !session.HandlePassengerInput(player, mover, controller,
                Vector2.zero, 0f, Time.fixedDeltaTime);
            Vector2 beforeMove = mover.rb.position;
            mover.rb.velocity = Vector2.right * 2f;
            firstWorld.Physics.PrimarySpace.PhysicsScene.Simulate(.1f);
            Vector2 afterMove = mover.rb.position;
            mover.rb.velocity = Vector2.zero;
            result["passengerMovedByNativePhysics"] = Vector2.Distance(beforeMove, afterMove) > .01f;
            session.HandlePassengerInput(player, mover, controller, Vector2.zero, 0f, Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            yield return null;
            GameplayWorldRegistry.RegisterPresentation(surfacePresentation);
            bool registeredSurfaceVisible = !surfaceRenderer.forceRenderingOff;
            GameplayWorldRegistry.RefreshVisibility();
            result["surfacePresentationRemainsVisible"] = registeredSurfaceVisible && !surfaceRenderer.forceRenderingOff &&
                !GameplayWorldRegistry.GetPresentationRequiredForceRenderingOff(surfaceRenderer);
            result["cameraFollowsProjectedPassenger"] = CameraFollowsProjection(player,
                session.ProjectShipInteriorPosition(firstShip, mover.rb.position));
            result["pointerReturnsShipLocalPosition"] = PointerReturnsShipLocalPosition(
                controller, session, firstShip, new Vector2(1.5f, .5f));
        }
        else
        {
            result["passengerMovedByNativePhysics"] = false;
            result["cameraFollowsProjectedPassenger"] = false;
            result["pointerReturnsShipLocalPosition"] = false;
        }
        result["ordinaryMovementDelegated"] = movementDelegated;

        using (GameplayWorldRegistry.UseWorld(firstWorld.WorldKey))
        {
            result["doorFound"] = firstDoorModule.CanInteract(player);
            Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
            bool interacted = sender != null && sender.TryInteractTarget(firstDoorModule);
            firstDoorModule.Save();
            session.FindPiece(firstDoorPiece.PieceId, out _, out ShipPieceState doorState);
            result["doorInteracted"] = interacted;
            result["doorStatePersisted"] = firstDoorModule.Data.IsOpen && doorState?.DoorOpen == true &&
                secondDoorModule.Data.IsOpen == false;

        }
        JObject weaponHit = ApplyOrdinaryShipDoorWeaponHit(player, rightDoor, firstWorld,
            session, rightDoorPiece.PieceId);
        result["healthApplied"] = weaponHit.Value<bool>("oneHit") && weaponHit.Value<bool>("pieceHealthUpdated");
        result["ordinaryDamageWindowRejectedDuplicate"] = weaponHit.Value<bool>("duplicateRejected");
        result["ordinaryDamageBackend"] = weaponHit;

        bool zeroGravityRetained = false;
        bool zeroGravityNativeVelocity = false;
        bool zeroGravityNativePhysics = false;
        if (entered && mover?.rb != null && controller != null)
        {
            passenger.ShipId = secondShip.ShipId;
            passenger.LocalX = 1.5d;
            passenger.LocalY = .5d;
            passenger.Supported = true;
            bool enteredUnpoweredInterior = session.TryTransferPassengerToShipInteriorWorld(
                player, passenger, secondShip);
            double interiorGravity = session.GetShipInteriorWalkingGravity(secondShip, new Vector2(1.5f, .5f));
            bool delegated = !session.HandlePassengerInput(player, mover, controller,
                Vector2.zero, 0f, Time.fixedDeltaTime);
            mover.rb.velocity = Vector2.zero;
            Vector2 before = mover.rb.position;
            mover.MoveByInput(Vector2.right, .1f);
            zeroGravityNativeVelocity = mover.rb.velocity.x > 0f;
            secondWorld.Physics.PrimarySpace.PhysicsScene.Simulate(.1f);
            zeroGravityNativePhysics = mover.rb.position.x > before.x + .001f;
            zeroGravityRetained = enteredUnpoweredInterior && interiorGravity == 0d && delegated &&
                passenger.Supported && session.IsPlayerInShipInteriorWorld(player) &&
                GameplayWorldRegistry.GetItemWorldKey(player) == secondWorld.WorldKey;
            passenger.ShipId = firstShip.ShipId;
            passenger.LocalX = 1.5d;
            passenger.LocalY = .5d;
            passenger.Supported = true;
            session.TryTransferPassengerToShipInteriorWorld(player, passenger, firstShip);
        }
        result["zeroGravityInteriorRetainsWorld"] = zeroGravityRetained;
        result["zeroGravityUsesNativeMoverVelocity"] = zeroGravityNativeVelocity;
        result["zeroGravityMovesThroughNativePhysics"] = zeroGravityNativePhysics;

        JObject terrainTransfer = TransferShipTerrainPayload(firstWorld, secondWorld, player);
        foreach (JProperty property in terrainTransfer.Properties()) result[property.Name] = property.Value.DeepClone();

        session.ReleaseAllShipInteriorPresentations();
        JObject presentationCache = session.ShipViewCacheSnapshot();
        bool noItemProjection = (presentationCache["itemProjections"] as JArray)?.Count == 0;
        bool noChunkProjection = (presentationCache["chunkProjections"] as JArray)?.Count == 0;
        JObject animalSmoke = SpawnAndSaveOrdinaryShipWorldChicken(session, firstWorld);
        foreach (JProperty property in animalSmoke.Properties()) result[property.Name] = property.Value.DeepClone();
        result["authoritySurvivedViewUnload"] = noItemProjection && noChunkProjection &&
            session.TryGetShipInteriorItem(firstDoorPiece.PieceId, out Item retainedDoor) && retainedDoor == firstDoor &&
            MachineStillOwned(firstWorld.WorldKey, machines.Cultivator);
        uint simulationStepBefore;
        using (MachineWorld.UseScope(firstWorld.WorldKey)) simulationStepBefore = MachineWorld.TransportStep;
        MachineWorld.TickShip(firstShip.ShipId, Mathf.Max(.25f, MachineCatalog.Settings.TickSeconds * 2f));
        uint simulationStepAfter;
        using (MachineWorld.UseScope(firstWorld.WorldKey)) simulationStepAfter = MachineWorld.TransportStep;
        result["simulationAdvancedWithoutView"] = noItemProjection && noChunkProjection &&
            MachineStillOwned(firstWorld.WorldKey, machines.Cultivator) &&
            simulationStepAfter > simulationStepBefore;

        bool movedPlayerToRight = false;
        if (entered && mover?.rb != null && controller != null)
        {
            Vector2 rightSide = new(6.5f, .5f);
            mover.rb.position = rightSide;
            player.transform.position = rightSide;
            movementDelegated &= !session.HandlePassengerInput(player, mover, controller,
                Vector2.zero, 0f, Time.fixedDeltaTime);
            movedPlayerToRight = movementDelegated && Math.Abs(passenger.LocalX - rightSide.x) < .01d;
        }

        ShipPieceState splitFloor = FindFloor(firstShip, 1);
        result["identityAnchorPreservedAcrossStructureChanges"] = firstShip.IdentityAnchorPieceId == originalIdentityAnchor;
        bool splitDamageAccepted = session.ApplyPieceDamage(int.Parse(splitFloor.PieceId,
            CultureInfo.InvariantCulture), 100000f, "隔离船内世界结构拆分采样");
        bool splitCommitted = false;
        for (int i = 0; i < 240 && !splitCommitted; i++)
        {
            yield return new WaitForFixedUpdate();
            session.FindPiece(rightDoorPiece.PieceId, out ShipState rightOwner, out _);
            splitCommitted = splitDamageAccepted && rightOwner != null && rightOwner != firstShip &&
                GameplayWorldRegistry.GetItemWorldKey(player) == MachineWorld.GetShipScopeKey(rightOwner.ShipId);
        }

        session.FindPiece(rightDoorPiece.PieceId, out ShipState migratedShip, out _);
        string migratedWorldKey = migratedShip != null ? MachineWorld.GetShipScopeKey(migratedShip.ShipId) : null;
        Item migratedDoor = null;
        bool doorItemMoved = migratedShip != null && session.TryGetShipInteriorItem(rightDoorPiece.PieceId, out migratedDoor);
        MachineEntity migratedCultivator = RebindMachineNode(migratedShip, machines.Cultivator);
        MachineEntity migratedTank = RebindMachineNode(migratedShip, machines.Tank);
        MachineEntity migratedPipe = RebindMachineNode(migratedShip, machines.Pipe);
        MachineEntity migratedWire = RebindMachineNode(migratedShip, machines.Wire);
        MachineEntity migratedPower = RebindMachineNode(migratedShip, machines.PowerSource);
        MachineEntity migratedGravity = RebindMachineNode(migratedShip, machines.GravityGenerator);
        if (migratedShip != null) MachineWorld.TickShip(migratedShip.ShipId, .02f);
        decimal migratedTankPayload = ReadTankPayload(migratedWorldKey, migratedTank);
        result["splitMovedPlayerToLocalWorld"] = movedPlayerToRight && splitCommitted &&
            GameplayWorldRegistry.GetItemWorldKey(player) == migratedWorldKey && mover?.rb?.gameObject.scene ==
            (migratedShip != null && session.TryGetShipInteriorWorld(migratedShip.ShipId, out ShipInteriorWorldRuntime migratedWorld)
                ? migratedWorld.Scene : default);
        result["transferHadNoDuplicate"] = splitCommitted && doorItemMoved && ReferenceEquals(migratedDoor, rightDoor) &&
            ReferenceEquals(migratedCultivator, machines.Cultivator) && ReferenceEquals(migratedTank, machines.Tank) &&
            ReferenceEquals(migratedPipe, machines.Pipe) && ReferenceEquals(migratedWire, machines.Wire) &&
            ReferenceEquals(migratedPower, machines.PowerSource) && ReferenceEquals(migratedGravity, machines.GravityGenerator) &&
            ItemGuidHasOneRuntimeInstance(rightDoor.itemData.Guid, rightDoor) &&
            migratedTankPayload == machines.TankPayload && migratedWorldKey != firstWorld.WorldKey;
        result["fragmentMachineWorldKey"] = migratedTank?.ScopeKey;
        result["splitDamageAccepted"] = splitDamageAccepted;
        result["splitFloorDestroyed"] = splitFloor.Destroyed;
        result["fragmentVisible"] = migratedShip != null && session.IsVisible(migratedShip);
        result["finalPlayerWorld"] = GameplayWorldRegistry.GetItemWorldKey(player);
        result["finalPlayerScene"] = player.gameObject.scene.name;
        result["fragmentPayloadPreserved"] = result.Value<bool>("transferHadNoDuplicate") &&
            migratedTankPayload > 0m && ElectricalGridContains(migratedWorldKey, migratedWire, migratedPower, migratedGravity);

        ShipState sacrificialShip = AddFirstFloor(fixture, "ShipFloor_Iron", OffsetFromPlayer(player, session, 90f));
        session.EnsureShipInteriorWorld(sacrificialShip);
        string sacrificialKey = MachineWorld.GetShipScopeKey(sacrificialShip.ShipId);
        ShipPieceState sacrificialFloor = sacrificialShip.Pieces[0];
        session.RollbackPiece(sacrificialShip, sacrificialFloor);
        yield return null;
        result["destroyReleasedWorlds"] = !session.TryGetShipInteriorWorld(sacrificialShip.ShipId, out _) &&
            !GameplayWorldRegistry.TryGet(sacrificialKey, out _) && session.GetShip(sacrificialShip.ShipId) == null &&
            !time.WorldTimeDict.ContainsKey(sacrificialKey) && !time.SceneLightingRateDict.ContainsKey(sacrificialKey);

        result["fixtureDropsRemoved"] = fixture.RemoveFixtureDrops();
        completed(result);
    }

    // 走普通生物入口验证船域归属、原生物理、存档与无投影模拟。
    private static JObject SpawnAndSaveOrdinaryShipWorldChicken(SpaceSession session, ShipInteriorWorldRuntime world)
    {
        var result = new JObject();
        int guid = 0;
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        try
        {
            Vector3 position = new(6.5f, .5f, 0f);
            if (!AiRuntimeBackendService.TrySpawnDirect("Chicken", position, 0, out guid) || guid == 0)
                throw new InvalidOperationException("普通生物后端未能在船内生成小鸡。");
            Item chicken = ItemMgr.Instance.GetItemByGuid(guid);
            if (chicken == null || !AiRuntimeBackendService.TryGetGameObjectActor(chicken, out IAIActor actor) ||
                !actor.IsAlive)
                throw new InvalidOperationException("船内小鸡未交付为已初始化的普通 AI Item。");

            result["ordinaryChickenSpawned"] = true;
            result["ordinaryChickenHasWorldLedger"] = GameplayWorldRegistry.GetItemWorldKey(chicken) == world.WorldKey;
            Rigidbody2D body = chicken.GetComponentInChildren<Rigidbody2D>(true);
            Collider2D collider = chicken.GetComponentInChildren<Collider2D>(true);
            Physics2D.SyncTransforms();
            result["ordinaryChickenUsesShipNativePhysics"] = chicken.gameObject.scene == world.Scene &&
                body != null && collider != null && body.gameObject.scene == world.Scene && body.simulated &&
                PhysicsSceneContainsCollider(world.Physics.PrimarySpace.PhysicsScene, collider);

            session.ReleaseAllShipInteriorPresentations();
            JArray projections = session.ShipViewCacheSnapshot()["itemProjections"] as JArray;
            bool hasProjection = false;
            if (projections != null)
                foreach (JObject projection in projections)
                    if (projection.Value<int>("sourceId") == chicken.gameObject.GetInstanceID())
                    { hasProjection = true; break; }
            chicken.Tick(Time.fixedDeltaTime);
            result["ordinaryChickenTickedWithoutView"] = !hasProjection && chicken.IsInitialized &&
                !chicken.ShouldUseSimulationRange() && GameplayWorldRegistry.SimulatesWithoutObservers(world.WorldKey) &&
                AiRuntimeBackendService.TryGetGameObjectActor(chicken, out IAIActor activeActor) && activeActor.IsAlive &&
                ReferenceEquals(ItemMgr.Instance.GetItemByGuid(guid), chicken);

            SaveDataMgr save = SaveDataMgr.Instance ?? throw new InvalidOperationException("普通生物存档服务尚未就绪。");
            save.CaptureRuntimeLocalWorldState(world.WorldKey);
            RuntimeWorldAddress address = world.Chunks.ResolveWorldAddress(chicken.transform.position);
            bool saved = false;
            if (save.TryGetChunkDelta(world.WorldKey,
                    new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y), out ChunkSaveRecord record) &&
                record?.ChangedItems != null)
                foreach (ItemData snapshot in record.ChangedItems)
                    if (snapshot?.Guid == guid && snapshot.IDName == "Chicken")
                    { saved = true; break; }
            result["ordinaryChickenSavedInLocalChunk"] = saved;
        }
        finally
        {
            if (guid != 0) AiRuntimeBackendService.TryDespawnActor(guid);
        }
        result["ordinaryChickenDespawned"] = guid != 0 && ItemMgr.Instance.GetItemByGuid(guid) == null;
        return result;
    }

    private static bool PhysicsSceneContainsCollider(PhysicsScene2D physicsScene, Collider2D target)
    {
        if (!physicsScene.IsValid() || target == null) return false;
        Collider2D[] overlaps = new Collider2D[16];
        int count = physicsScene.OverlapPoint(target.bounds.center,
            new ContactFilter2D { useTriggers = true }, overlaps);
        for (int i = 0; i < count; i++)
            if (ReferenceEquals(overlaps[i], target)) return true;
        return false;
    }

    private static JObject ShipWorldSnapshot(SpaceSession session, ShipState ship,
        ShipInteriorWorldRuntime world, Item item)
    {
        int readyChunks = 0;
        foreach (RuntimeWorldAddress address in world.ChunkAddresses)
            if (world.WorldRuntime.TryGetChunkTerrain(address, out ChunkTerrainData terrain) &&
                terrain != null && !terrain.IsDisposed) readyChunks++;
        return new JObject
        {
            ["shipId"] = ship.ShipId, ["worldKey"] = world.WorldKey,
            ["worldRuntimeId"] = world.WorldRuntime.WorldId,
            ["readyChunkCount"] = readyChunks,
            ["itemWorldKey"] = GameplayWorldRegistry.GetItemWorldKey(item),
            ["itemGuid"] = item.itemData.Guid,
            ["itemRuntimeRegistered"] = ItemMgr.Instance.IsRuntimeItemRegistered(item)
        };
    }

    private static bool ReadShipFloorSupport(ShipInteriorWorldRuntime world)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        return world.Chunks.TryGetRuntimeTerrainTile(new Vector2(.5f, .5f), out RuntimeTerrainTileSample sample) &&
            sample.Cell.IsWalkable && sample.TopTileId > 0 &&
            TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) > 0;
    }

    private static bool ReadExternalVisualSuppression(ShipInteriorWorldRuntime world)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        return world.Chunks.TryGetRuntimeTerrainTile(new Vector2(.5f, .5f), out RuntimeTerrainTileSample sample) &&
            sample.Terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalVisualLayer,
                sample.LocalCell.x, sample.LocalCell.y, out float value) && value == 1f;
    }

    // 以完整目录验证普通 GroundPlacement 定义可推断为船内地板，不依赖专用部件名单。
    private static bool ReadOrdinaryGroundPlacementInference()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null) return false;
        foreach (string itemId in resources.GetAllItemIds())
        {
            if (!resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) ||
                !definition.HasModule(ModText.Building)) continue;
            ItemData data = resources.CreateItemData(itemId);
            if (!Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building))
                continue;
            RuntimeTileDefinition tile = resources.GetTileBlock(building.TileBlockId);
            if (tile?.GroundPlacement == null || Mod_ShipPart.Read(data) != null) continue;
            ShipPartConfiguration inferred = SpaceSession.ResolvePartConfiguration(data);
            if (inferred?.Kind == ShipPieceKind.Floor && inferred.SupportTileBlockId == tile.Id)
                return true;
        }
        return false;
    }

    // 正式伤害窗口扫描和显式碰撞都走同一真实武器结算。
    private static JObject ApplyOrdinaryShipDoorWeaponHit(Player attacker, Item target, ShipInteriorWorldRuntime world,
        SpaceSession session, string targetPieceId)
    {
        Item weapon = null;
        Mod_Damage damage = null;
        var result = new JObject();
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        try
        {
            weapon = ItemMgr.Instance.InstantiateHeldItem(
                GameRes.ExistingInstance.CreateItemData("Axe_Bronze"), attacker, attacker.transform);
            weapon.Owner = attacker;
            weapon.Load();
            weapon.SetInHand(true);
            damage = weapon.GetComponentInChildren<Mod_Damage>(true);
            Mod_DamageReceiver receiver = target?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            Collider2D receiverCollider = receiver != null ? receiver.GetComponent<Collider2D>() : null;
            if (damage == null || receiver == null || receiverCollider == null || damage.DamageCollider == null ||
                !CombatPhysicsChannels.IsDamageReceiverCollider(receiverCollider) ||
                !ReferenceEquals(weapon.Owner, attacker) || !weapon.InHand ||
                GameplayWorldRegistry.GetItemWorldKey(weapon) != world.WorldKey)
                throw new InvalidOperationException("真实青铜斧或船内目标没有进入标准伤害通道。");

            damage.BindToWeaponRenderer(weapon.Sprite);
            weapon.transform.position += receiverCollider.bounds.center - damage.DamageCollider.bounds.center;
            Physics2D.SyncTransforms();
            float before = receiver.Hp;
            damage.StartAttack();
            float afterWindowScan = receiver.Hp;
            if (afterWindowScan >= before)
                damage.ProcessExplicitColliderHit(receiverCollider, damage.DamageOrigin, receiverCollider.bounds.center);
            float afterFirstHit = receiver.Hp;
            damage.ProcessExplicitColliderHit(receiverCollider, damage.DamageOrigin, receiverCollider.bounds.center);
            float afterDuplicate = receiver.Hp;
            damage.StopAttack();
            session.FindPiece(targetPieceId, out _, out ShipPieceState piece);
            result["weapon"] = weapon.itemData.IDName;
            result["receiverColliderLayer"] = LayerMask.LayerToName(receiverCollider.gameObject.layer);
            result["beforeHp"] = before;
            result["afterHp"] = afterFirstHit;
            result["afterDuplicateHp"] = afterDuplicate;
            result["oneHit"] = afterFirstHit < before && Mathf.Approximately(afterDuplicate, afterFirstHit);
            result["duplicateRejected"] = afterFirstHit < before && Mathf.Approximately(afterDuplicate, afterFirstHit);
            result["pieceHealthUpdated"] = piece != null && piece.Health < before;
            result["windowScanHit"] = afterWindowScan < before;
            return result;
        }
        finally
        {
            damage?.StopAttack();
            if (weapon != null) ItemMgr.Instance.DespawnItem(weapon, saveData: false);
        }
    }

    // 用真实地层、ECS 作物和液体记录验证事务的回滚与反向清理。
    private static JObject TransferShipTerrainPayload(ShipInteriorWorldRuntime sourceWorld,
        ShipInteriorWorldRuntime destinationWorld, Player actor)
    {
        Vector2 cellCenter = new(1.5f, .5f);
        if (!GameRes.ExistingInstance.TryGetTileDefinition("Tile_Farmland", out RuntimeTileDefinition soil) ||
            !GameRes.ExistingInstance.TryGetItemDefinition("RadishCrop", out RuntimeItemDefinition crop) ||
            !crop.UsesResourceEntities)
            throw new InvalidOperationException("土层事务缺少正式土壤或 Entity 作物定义。");

        RuntimeTerrainTileSample sourceSample;
        RuntimeTerrainTileSample destinationSample;
        GroundLayerCellSaveData originalGround;
        TerrainCell originalCell;
        LiquidCellSaveData originalLiquid;
        ChunkAgricultureRenderer sourceAgriculture;
        using (GameplayWorldRegistry.UseWorld(sourceWorld.WorldKey))
        {
            if (!sourceWorld.Chunks.TryGetRuntimeTerrainTile(cellCenter, out sourceSample) ||
                sourceSample.Terrain.GetCell(sourceSample.LocalCell.x, sourceSample.LocalCell.y).GroundTileId != 0 ||
                StoredLiquidLevel(sourceSample) != 0 ||
                ReadCropGuid(sourceSample) != 0)
                throw new InvalidOperationException("船内土层事务需要一个没有内容的源格。");
            originalCell = sourceSample.Terrain.GetCell(sourceSample.LocalCell.x, sourceSample.LocalCell.y);
            originalGround = GroundTileLayerSystem.Capture(sourceSample.Terrain, sourceSample.LocalCell);
            originalLiquid = LiquidCellSaveData.Capture(sourceSample.Terrain,
                sourceSample.LocalCell.x, sourceSample.LocalCell.y);
            if (originalGround.Covers.Count != 0 || sourceSample.Terrain.TryGetEnvironmentValue(
                    FarmlandSystem.SourceLayer, sourceSample.LocalCell.x, sourceSample.LocalCell.y, out float soilState) && soilState != 0f ||
                HasAgricultureRecord(sourceSample))
                throw new InvalidOperationException("测试土层格已有玩家覆盖或耕地记录。");
            sourceWorld.Chunks.EnsureRuntimeChunkViewBoundForStateTransfer(sourceSample.Address);
            if (!sourceWorld.Chunks.TryGetRuntimeChunkView(sourceSample.Address, out ChunkView sourceView) ||
                (sourceAgriculture = sourceView.GetComponentInChildren<ChunkAgricultureRenderer>(true)) == null)
                throw new InvalidOperationException("船内源区块没有正式农业渲染器。");
        }
        using (GameplayWorldRegistry.UseWorld(destinationWorld.WorldKey))
        {
            if (!destinationWorld.Chunks.TryGetRuntimeTerrainTile(cellCenter, out destinationSample) ||
                destinationSample.Terrain.GetCell(destinationSample.LocalCell.x, destinationSample.LocalCell.y).GroundTileId != 0 ||
                StoredLiquidLevel(destinationSample) != 0 ||
                ReadCropGuid(destinationSample) != 0)
                throw new InvalidOperationException("船内土层事务需要一个没有内容的目标格。");
            if (HasAgricultureRecord(destinationSample))
                throw new InvalidOperationException("测试土层格已有农业持久化记录。");
        }

        NaturalEntityHandle initialCrop;
        int cropGuid;
        using (GameplayWorldRegistry.UseWorld(sourceWorld.WorldKey))
        {
            if (!GroundTileLayerSystem.TryApplyPlayerCover(sourceSample, soil, out string reason) ||
                !sourceWorld.Chunks.TryGetRuntimeTerrainTile(cellCenter, out sourceSample) ||
                !WorldLiquidSystem.TrySet(sourceSample, "core:pure_water", 3))
                throw new InvalidOperationException("真实玩家土层覆盖或三级水注入失败：" + reason);
            initialCrop = sourceAgriculture.CreateEntityCrop(sourceSample.WorldCell, crop);
            sourceAgriculture.CaptureEntityCrop(initialCrop);
            cropGuid = ReadCropGuid(sourceSample);
            if (!NaturalEntityEcsService.Contains(initialCrop) || cropGuid <= 0)
                throw new InvalidOperationException("作物没有正式 ECS 实例或稳定 GUID。");
        }

        bool SelectCell(Vector2 center) => Vector2.Distance(center, cellCenter) < .01f;
        Vector2 ProjectCell(Vector2 center) => center;
        bool transactionAppliedBeforeDispose;
        using (SaveDataMgr.Instance.TransferWorldTerrain(sourceWorld.WorldKey, destinationWorld.WorldKey,
                   SelectCell, ProjectCell))
        {
            var from = ReadTerrainPayload(sourceWorld, cellCenter);
            var to = ReadTerrainPayload(destinationWorld, cellCenter);
            transactionAppliedBeforeDispose = from.GroundTile == 0 && from.CropGuid == 0 &&
                string.IsNullOrEmpty(from.LiquidId) && from.LiquidLevel == 0 &&
                to.GroundTile == soil.RuntimeTileId && to.CropGuid == cropGuid &&
                to.LiquidId == "core:pure_water" && to.LiquidLevel == 3;
        }
        var afterRollbackSource = ReadTerrainPayload(sourceWorld, cellCenter);
        var afterRollbackDestination = ReadTerrainPayload(destinationWorld, cellCenter);
        bool rollbackSamePayload = afterRollbackSource.GroundTile == soil.RuntimeTileId &&
            afterRollbackSource.CropGuid == cropGuid && afterRollbackSource.LiquidId == "core:pure_water" &&
            afterRollbackSource.LiquidLevel == 3 &&
            afterRollbackDestination.GroundTile == 0 && afterRollbackDestination.CropGuid == 0 &&
            string.IsNullOrEmpty(afterRollbackDestination.LiquidId) && afterRollbackDestination.LiquidLevel == 0;

        bool committedToDestination;
        using (SaveDataMgr.WorldTerrainTransfer committed = SaveDataMgr.Instance.TransferWorldTerrain(
                   sourceWorld.WorldKey, destinationWorld.WorldKey, SelectCell, ProjectCell))
        {
            var from = ReadTerrainPayload(sourceWorld, cellCenter);
            var to = ReadTerrainPayload(destinationWorld, cellCenter);
            committedToDestination = from.GroundTile == 0 && from.CropGuid == 0 && from.LiquidLevel == 0 &&
                to.GroundTile == soil.RuntimeTileId && to.CropGuid == cropGuid &&
                to.LiquidId == "core:pure_water" && to.LiquidLevel == 3;
            committed.Commit();
        }

        using (var reverse = SaveDataMgr.Instance.TransferWorldTerrain(destinationWorld.WorldKey,
                   sourceWorld.WorldKey, SelectCell, ProjectCell)) reverse.Commit();
        var afterReverseSource = ReadTerrainPayload(sourceWorld, cellCenter);
        var afterReverseDestination = ReadTerrainPayload(destinationWorld, cellCenter);
        bool reversedToSource = afterReverseSource.GroundTile == soil.RuntimeTileId &&
            afterReverseSource.CropGuid == cropGuid && afterReverseSource.LiquidId == "core:pure_water" &&
            afterReverseSource.LiquidLevel == 3 && afterReverseDestination.GroundTile == 0 &&
            afterReverseDestination.CropGuid == 0 && afterReverseDestination.LiquidLevel == 0;

        bool cropRemoved = RollbackTransferredCrop(sourceWorld, actor, crop.Id, cropGuid, cellCenter);
        bool cleaned = cropRemoved && RestoreEmptyTerrainCell(sourceWorld, cellCenter, originalGround,
            originalCell, originalLiquid);
        var cleanSource = ReadTerrainPayload(sourceWorld, cellCenter);
        var cleanDestination = ReadTerrainPayload(destinationWorld, cellCenter);
        bool cleanupRestoredOriginal = cleaned && cleanSource.GroundTile == originalCell.GroundTileId &&
            cleanSource.CropGuid == 0 && cleanSource.LiquidLevel == originalLiquid.LiquidLevel &&
            cleanDestination.GroundTile == 0 && cleanDestination.CropGuid == 0 &&
            string.IsNullOrEmpty(cleanDestination.LiquidId) &&
            cleanDestination.LiquidLevel == 0;

        return new JObject
        {
            ["terrainTransactionAppliedBeforeDispose"] = transactionAppliedBeforeDispose,
            ["terrainRollbackKeptSoilCropAndLiquid"] = rollbackSamePayload,
            ["terrainCommitMovedSoilCropAndLiquid"] = committedToDestination,
            ["terrainReverseReturnedPayload"] = reversedToSource,
            ["terrainTransferReversedAndCleaned"] = cleanupRestoredOriginal
        };
    }

    private static (int GroundTile, int CropGuid, string LiquidId, int LiquidLevel) ReadTerrainPayload(
        ShipInteriorWorldRuntime world, Vector2 cellCenter)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        if (!world.Chunks.TryGetRuntimeTerrainTile(cellCenter, out RuntimeTerrainTileSample sample))
            throw new InvalidOperationException("船内土层事务丢失目标格。");
        TerrainCell rawCell = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
        return (rawCell.GroundTileId, ReadCropGuid(sample), sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y),
            sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y));
    }

    private static int ReadCropGuid(RuntimeTerrainTileSample sample)
    {
        foreach (AgricultureCellSaveData state in SaveDataMgr.Instance.GetAgricultureCells(sample.Address))
            if (state.LocalPosition == sample.LocalCell) return state.Crop?.Guid ?? 0;
        return 0;
    }

    private static bool HasAgricultureRecord(RuntimeTerrainTileSample sample)
    {
        foreach (AgricultureCellSaveData state in SaveDataMgr.Instance.GetAgricultureCells(sample.Address))
            if (state.LocalPosition == sample.LocalCell) return true;
        return false;
    }

    private static int StoredLiquidLevel(RuntimeTerrainTileSample sample)
        => sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);

    private static bool RollbackTransferredCrop(ShipInteriorWorldRuntime world, Player actor,
        string cropId, int cropGuid, Vector2 cellCenter)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        if (!world.Chunks.TryGetRuntimeTerrainTile(cellCenter, out RuntimeTerrainTileSample sample) ||
            !world.Chunks.TryGetRuntimeChunkView(sample.Address, out ChunkView view) ||
            view.GetComponentInChildren<ChunkAgricultureRenderer>(true) is not ChunkAgricultureRenderer agriculture)
            return false;
        var observations = new List<NaturalEntityTargetObservation>();
        NaturalEntityEcsService.QueryTargetObservations(actor, 4f, observations);
        foreach (NaturalEntityTargetObservation observation in observations)
        {
            if (observation.ItemId != cropId || observation.Target?.TargetGuid != cropGuid) continue;
            agriculture.RollbackEntityCrop(observation.Handle);
            SaveDataMgr.Instance.RecordCultivatedCropData(sample.Address, sample.LocalCell, null);
            return true;
        }
        return false;
    }

    private static bool RestoreEmptyTerrainCell(ShipInteriorWorldRuntime world, Vector2 cellCenter,
        GroundLayerCellSaveData ground, TerrainCell originalCell, LiquidCellSaveData liquid)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        if (!world.Chunks.TryGetRuntimeTerrainTile(cellCenter, out RuntimeTerrainTileSample sample) ||
            !world.Chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk)) return false;
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        foreach (string layer in new[] { FarmlandSystem.SourceLayer, FarmlandSystem.WaterLayer,
                     FarmlandSystem.WaterSupplyRemainderLayer, FarmlandSystem.WaterConsumptionRemainderLayer,
                     FarmlandSystem.FertilityLayer })
            if (sample.Terrain.TryGetEnvironmentValue(layer, x, y, out float value) && value != 0f)
                sample.Terrain.SetEnvironmentValue(layer, x, y, 0f);
        GroundTileLayerSystem.Restore(sample.Terrain, ground);
        sample.Terrain.SetCell(x, y, originalCell);
        if (!WorldLiquidSystem.TrySet(sample, liquid.LiquidId, liquid.LiquidLevel)) return false;
        SaveDataMgr.Instance.RecordLiquidChange(sample);
        SaveDataMgr.Instance.RecordGroundLayerCell(sample);
        SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell,
            sample.LocalCell, originalCell.GroundTileId, FarmlandSystem.GetBlockId(originalCell.GroundTileId)));
        if (!world.Chunks.TryGetRuntimeTerrainTile(cellCenter, out sample)) return false;
        bool agricultureRemoved = SaveDataMgr.Instance.TryRemoveUninitializedAgricultureCell(sample);
        return agricultureRemoved && !HasAgricultureRecord(sample) && ReadCropGuid(sample) == 0 &&
            StoredLiquidLevel(sample) == liquid.LiquidLevel &&
            sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId == originalCell.GroundTileId;
    }

    private static JObject PlaceAndRemoveOrdinaryShipWall(ShipInteriorFixture fixture, ShipState ship,
        ShipInteriorWorldRuntime world, Player actor)
    {
        ItemData source = GameRes.ExistingInstance.CreateItemData("Wall_Iron_Summoner");
        source.Stack.Amount = 1f;
        var slot = new ItemSlot(0) { itemData = source };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "船内标准墙壁建造材料");
        Vector2 position = fixture.Session.DisplayPoint(ship, 1.5d, .5d);
        var request = new FlatWorld.Gameplay.Building.BuildingPlacementRequest
        {
            SourceData = source, SourceInventory = inventory, SourceSlot = slot, Actor = actor,
            Position = new Vector3(position.x, position.y, 0f),
            AuthorityPosition = actor.transform.position, MaximumDistance = 1000f
        };
        using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
        if (!FlatWorld.Gameplay.Building.BuildingPlacementService.TryPlace(request,
                out FlatWorld.Gameplay.Building.BuildingPlacementResult placed, out string reason))
            throw new InvalidOperationException("正式普通墙壁建造后端失败：" + reason);

        string wallPieceId = placed.PlacedData.Guid.ToString(CultureInfo.InvariantCulture);
        fixture.TrackPiece(wallPieceId);
        Mod_Building.TryReadBuildingData(placed.PlacedData, out _, out Mod_Building.Building_Data wallData);
        if (wallData == null || string.IsNullOrWhiteSpace(wallData.TileBlockId) ||
            !world.Chunks.TryGetRuntimeTerrainTile(new Vector2(1.5f, .5f), out RuntimeTerrainTileSample sample) ||
            !world.Chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            throw new InvalidOperationException("标准墙壁后端没有提交到真实船内地形。");
        int wallTile = sample.Cell.BlockingTileId;
        bool mirrored = wallTile > 0 && wallTile == GameRes.ExistingInstance.GetTileBlock(wallData.TileBlockId).RuntimeTileId &&
            fixture.Session.FindPiece(wallPieceId, out _, out ShipPieceState wallPiece) &&
            wallPiece.Kind == ShipPieceKind.Wall;
        var placedCell = new TileBuildingCell(chunk, sample.WorldCell, sample.LocalCell, wallTile, wallData.TileBlockId);
        bool removed = mirrored && TileBuildingSystem.TryRemove(placedCell, false, out _);
        bool structureUpdated = removed && !fixture.Session.FindPiece(wallPieceId, out _, out _);
        return new JObject
        {
            ["placementBackend"] = "BuildingPlacementService/ship-local-grid",
            ["wallTileId"] = wallTile, ["ordinaryTileWallMirrored"] = mirrored,
            ["ordinaryTileRemovalUpdatedStructure"] = structureUpdated
        };
    }

    private static JObject CreateAndSimulateNativeShipPhysics(ShipInteriorFixture fixture,
        ShipInteriorWorldRuntime first, ShipInteriorWorldRuntime second)
    {
        // 远离夹具本身的门和机器，只比较两份空间对同一个局部坐标的碰撞。
        Vector2 origin = new(16f, 8f);
        GameObject wall = NewPhysicsObject(fixture, first.Scene, "ship-a-local-wall", origin + Vector2.right, true);
        wall.GetComponent<BoxCollider2D>().size = new Vector2(.25f, 5f);
        Rigidbody2D blocked = NewPhysicsBody(fixture, first.Scene, "ship-a-local-body", origin);
        Rigidbody2D free = NewPhysicsBody(fixture, second.Scene, "ship-b-local-body", origin);
        Physics2D.SyncTransforms();
        blocked.velocity = Vector2.right * 3f;
        free.velocity = Vector2.right * 3f;
        PhysicsScene2D firstPhysics = first.Physics.PrimarySpace.PhysicsScene;
        PhysicsScene2D secondPhysics = second.Physics.PrimarySpace.PhysicsScene;
        for (int i = 0; i < 60; i++)
        {
            firstPhysics.Simulate(.02f);
            secondPhysics.Simulate(.02f);
        }
        blocked.velocity = Vector2.zero;
        free.velocity = Vector2.zero;
        Collider2D firstHit = firstPhysics.OverlapPoint(origin + Vector2.right);
        Collider2D secondHit = secondPhysics.OverlapPoint(origin + Vector2.right);
        return new JObject
        {
            ["firstShipCollided"] = blocked.position.x < origin.x + .7f,
            ["secondShipPassed"] = free.position.x > origin.x + 1.5f,
            ["nativeQueryIsolated"] = firstHit == wall.GetComponent<Collider2D>() && secondHit == null,
            ["firstPosition"] = blocked.position.x, ["secondPosition"] = free.position.x
        };
    }

    private static GameObject NewPhysicsObject(ShipInteriorFixture fixture, Scene scene, string name,
        Vector2 position, bool isStatic)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        value.transform.position = position;
        Rigidbody2D body = value.AddComponent<Rigidbody2D>();
        body.bodyType = isStatic ? RigidbodyType2D.Static : RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        value.AddComponent<BoxCollider2D>();
        fixture.PhysicsObjects.Add(value);
        return value;
    }

    private static Rigidbody2D NewPhysicsBody(ShipInteriorFixture fixture, Scene scene, string name, Vector2 position)
    {
        GameObject value = NewPhysicsObject(fixture, scene, name, position, false);
        value.GetComponent<BoxCollider2D>().size = Vector2.one * .5f;
        Rigidbody2D body = value.GetComponent<Rigidbody2D>();
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        return body;
    }

    private static ShipState AddFirstFloor(ShipInteriorFixture fixture, string itemId, Vector2 displayPosition)
    {
        AddShipPiece(fixture, itemId, displayPosition, null, out ShipState ship);
        if (ship == null) throw new InvalidOperationException("正式地板事务没有创建船体。");
        return ship;
    }

    private static ShipPieceState AddShipPiece(ShipInteriorFixture fixture, string itemId,
        Vector2 position, ShipState targetShip)
        => AddShipPiece(fixture, itemId, position, targetShip, out _);

    private static ShipPieceState AddShipPiece(ShipInteriorFixture fixture, string itemId,
        Vector2 position, ShipState targetShip, out ShipState ship)
    {
        ItemData data = GameRes.ExistingInstance.CreateItemData(itemId);
        data.Stack.Amount = 1f;
        Mod_Building.SetInstalledDataState(data);
        ShipPieceState piece = fixture.Session.AddPiece(data, position, 0, out ship, targetShip: targetShip);
        fixture.TrackPiece(piece.PieceId);
        fixture.Session.CommitPiece(ship, piece);
        return piece;
    }

    private static void AddFloorLine(ShipInteriorFixture fixture, ShipState ship, int firstCell, int lastCell)
    {
        for (int x = firstCell; x <= lastCell; x++)
            AddShipPiece(fixture, "ShipFloor_Iron", new Vector2(x + .5f, .5f), ship);
    }

    private static void AddElectricalAndFluidGrid(ShipInteriorFixture fixture, ShipState ship, string suffix,
        bool includeGravityGenerator = true)
    {
        AddShipPiece(fixture, "Battery_LithiumIronPhosphate", new Vector2(2.5f, .5f), ship);
        AddShipPiece(fixture, "Wire_Copper", new Vector2(2.5f, .5f), ship);
        AddShipPiece(fixture, "Creative_Power_Source", new Vector2(3.5f, .5f), ship);
        AddShipPiece(fixture, "Wire_Copper", new Vector2(3.5f, .5f), ship);
        AddShipPiece(fixture, "Wire_Copper", new Vector2(4.5f, .5f), ship);
        if (includeGravityGenerator)
            AddShipPiece(fixture, "ShipGravityGenerator", new Vector2(4.5f, .5f), ship);
        AddShipPiece(fixture, "SpaceCultivator", new Vector2(5.5f, .5f), ship);
        AddShipPiece(fixture, "FluidPipe", new Vector2(6.5f, .5f), ship);
        ShipPieceState tankPiece = AddShipPiece(fixture, "GasTankBlock_Steel", new Vector2(7.5f, .5f), ship);
        MachineFixture machines = GetMachineFixture(fixture.Session, ship, suffix, includeGravityGenerator);
        machines.Tank = FindMachine(ship, tankPiece);
        using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId)))
        {
            FluidInventory inventory = MachineWorld.GetFluidInventory(machines.Tank);
            FluidDefinition oxygen = FluidCatalog.Default.Find(FluidIds.Oxygen);
            FluidBatch payload = FluidInventory.CreateBatch(oxygen,
                FluidUnits.StandardLitersToMol(suffix == "first" ? 11m : 17m), 0m, 293.15d);
            if (!inventory.TryAdd(payload, machines.Tank.Definition.Fluid.VolumeLiters + .001d, .001d))
                throw new InvalidOperationException("隔离船舶气罐载荷初始化失败。");
            MachineWorld.CaptureSnapshot(machines.Tank);
            machines.TankPayload = inventory.GetGasMoles(FluidIds.Oxygen);
        }
        fixture.MachineSnapshots.Add(ship.ShipId, machines);
    }

    private static MachineFixture GetMachineFixture(SpaceSession session, ShipState ship, string suffix,
        bool includeGravityGenerator = true)
    {
        MachineEntity Find(string itemId)
        {
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece.IsAlive && piece.ItemId == itemId) return FindMachine(ship, piece);
            throw new InvalidOperationException("船内机器夹具缺少部件：" + itemId + "/" + suffix);
        }
        return new MachineFixture
        {
            Cultivator = Find("SpaceCultivator"),
            Wire = Find("Wire_Copper"),
            PowerSource = Find("Creative_Power_Source"),
            GravityGenerator = includeGravityGenerator ? Find("ShipGravityGenerator") : null,
            Pipe = Find("FluidPipe")
        };
    }

    private static MachineEntity FindMachine(ShipState ship, ShipPieceState piece)
    {
        int id = int.Parse(piece.PieceId, CultureInfo.InvariantCulture);
        using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId)))
        {
            MachineEntity node = MachineWorld.GetById(id);
            if (node == null || node.ScopeKey != MachineWorld.GetShipScopeKey(ship.ShipId))
                throw new InvalidOperationException("正式船内机械账本缺少部件：" + piece.ItemId);
            return node;
        }
    }

    private static Item RequireInteriorItem(SpaceSession session, ShipPieceState piece)
    {
        if (!session.TryGetShipInteriorItem(piece.PieceId, out Item item))
            throw new InvalidOperationException("船内普通 Item 没有真实运行实例：" + piece.ItemId);
        return item;
    }

    private static ShipPieceState FindFloor(ShipState ship, int x)
    {
        foreach (ShipPieceState piece in ship.Pieces)
            if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor && piece.CellX == x && piece.CellY == 0)
                return piece;
        throw new InvalidOperationException("结构拆分采样缺少目标地板：" + x);
    }

    private static Vector2 OffsetFromPlayer(Player player, SpaceSession session, float distance)
    {
        Vector2 candidate = (Vector2)player.transform.position + Vector2.right * distance;
        return session.IsSpaceView ? candidate : WorldLocalPresentation.ProjectPosition(
            WorldLocalPresentation.ToLogical(candidate), player.transform.position);
    }

    private static bool MachineStillOwned(string worldKey, MachineEntity node)
    {
        if (node == null || node.ScopeKey != worldKey) return false;
        using (MachineWorld.UseScope(worldKey)) return ReferenceEquals(MachineWorld.GetById(node.Id), node);
    }

    private static bool TankPayloadMatches(string worldKey, MachineEntity node, decimal expected)
        => node != null && MachineStillOwned(worldKey, node) && ReadTankPayload(worldKey, node) == expected;

    private static decimal ReadTankPayload(string worldKey, MachineEntity node)
    {
        if (node == null || string.IsNullOrWhiteSpace(worldKey)) return 0m;
        using (MachineWorld.UseScope(worldKey)) return MachineWorld.GetFluidInventory(node).GetGasMoles(FluidIds.Oxygen);
    }

    private static bool ElectricalGridContains(string worldKey, MachineEntity wire,
        MachineEntity source, MachineEntity consumer)
    {
        if (wire == null || source == null || consumer == null) return false;
        using (MachineWorld.UseScope(worldKey))
        {
            foreach (ElectricalNetwork network in MachineWorld.ElectricalNetworks)
                if (network.Nodes.Contains(wire) && network.Nodes.Contains(source) && network.Nodes.Contains(consumer)) return true;
            return false;
        }
    }

    private static bool MachineVisualLayerMatches(MachineEntity node)
    {
        if (node == null || !MachineStillOwned(node.ScopeKey, node)) return false;
        using var scope = MachineWorld.UseScope(node.ScopeKey);
        MachineInteractionTarget target = MachineWorld.GetOrCreateInteractionTarget(node);
        return target != null && target.TryGetInteractionGridHit(node.Snapshot.transform.position,
            default, out GridInteractionHit hit) && hit.Layer == node.Definition.VisualLayer;
    }

    private static bool RegisteredPlayerChildrenUseWorld(Player player, string worldKey)
    {
        foreach (Item child in player.GetComponentsInChildren<Item>(true))
        {
            if (child == null || child == player || child.itemData == null ||
                !ItemMgr.Instance.WorldRunTimeItems.ContainsKey(child.itemData.Guid)) continue;
            if (GameplayWorldRegistry.GetItemWorldKey(child) != worldKey) return false;
        }
        return true;
    }

    private static bool CameraFollowsProjection(Player player, Vector2 displayPosition)
    {
        Mod_Cam camera = player.itemMods?.GetMod_ByID<Mod_Cam>(ModText.Camera);
        Transform follow = camera?.Vcam != null ? camera.Vcam.Follow : null;
        return camera?.HasFollowOverride == true && follow != null && follow != player.transform &&
            Vector2.Distance(follow.position, displayPosition) < .05f;
    }

    private static bool PointerReturnsShipLocalPosition(Mod_GameController controller,
        SpaceSession session, ShipState ship, Vector2 localPosition)
    {
        if (controller == null) return false;
        controller._mainCamera ??= Camera.main;
        if (controller._mainCamera == null) return false;
        Vector2 projected = session.ProjectShipInteriorPosition(ship, localPosition);
        Vector3 screen = controller._mainCamera.WorldToScreenPoint(new Vector3(projected.x, projected.y, 0f));
        return controller.TryGetMouseWorldPosition(screen, out Vector3 actual) &&
            Vector2.Distance(new Vector2(actual.x, actual.y), localPosition) < .02f;
    }

    private static bool ItemGuidHasOneRuntimeInstance(int guid, Item expected)
    {
        if (ItemMgr.GetExistingInstance() is not ItemMgr manager ||
            !manager.WorldRunTimeItems.TryGetValue(guid, out Item found) || !ReferenceEquals(found, expected)) return false;
        int matches = 0;
        foreach (Item item in manager.WorldRunTimeItems.Values)
            if (item?.itemData?.Guid == guid) matches++;
        return matches == 1;
    }

    private static MachineEntity RebindMachineNode(ShipState ship, MachineEntity previous)
    {
        if (ship == null || previous == null || !int.TryParse(previous.Id.ToString(CultureInfo.InvariantCulture), out int id)) return null;
        using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId))) return MachineWorld.GetById(id);
    }

    private sealed class MachineFixture
    {
        public MachineEntity Cultivator, Tank, Pipe, Wire, PowerSource, GravityGenerator;
        public decimal TankPayload;
    }

    private sealed class ShipInteriorFixture : IDisposable
    {
        private readonly string originalWorldKey;
        private readonly Scene originalScene;
        private readonly Vector3 originalPosition;
        private readonly Quaternion originalRotation;
        private readonly string originalSceneName;
        private readonly string originalPassengerJson;
        private readonly bool hadPassenger;
        private readonly Vector3 originalDataPosition;
        private readonly Quaternion originalDataRotation;
        private readonly Rigidbody2D body;
        private readonly Vector2 originalBodyVelocity;
        private readonly float originalAngularVelocity;
        private readonly float originalGravityScale;
        private readonly RigidbodyType2D originalBodyType;
        private readonly bool originalBodySimulated;
        private readonly HashSet<int> originalLooseGuids = new();

        public readonly SpaceSession Session;
        public readonly Player Player;
        public SpacePassengerState Passenger;
        public readonly List<string> PieceIds = new();
        public readonly List<GameObject> PhysicsObjects = new();
        public readonly Dictionary<string, MachineFixture> MachineSnapshots = new(StringComparer.Ordinal);

        public ShipInteriorFixture(SpaceSession session, Player player)
        {
            Session = session; Player = player;
            originalWorldKey = GameplayWorldRegistry.GetItemWorldKey(player);
            originalScene = player.gameObject.scene;
            originalPosition = player.transform.position;
            originalRotation = player.transform.rotation;
            originalSceneName = player.Data?.CurrentSceneName;
            originalDataPosition = player.itemData?.transform != null ? player.itemData.transform.position : originalPosition;
            originalDataRotation = player.itemData?.transform != null ? player.itemData.transform.rotation : originalRotation;
            SpacePassengerState previous = session.State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            hadPassenger = previous != null;
            originalPassengerJson = previous != null ? JsonConvert.SerializeObject(previous) : null;
            body = player.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.rb;
            originalBodyVelocity = body != null ? body.velocity : Vector2.zero;
            originalAngularVelocity = body != null ? body.angularVelocity : 0f;
            originalGravityScale = body != null ? body.gravityScale : 0f;
            originalBodyType = body != null ? body.bodyType : RigidbodyType2D.Dynamic;
            originalBodySimulated = body != null && body.simulated;
            foreach (SpaceLooseItemState loose in session.State.LooseItems)
            {
                ItemData data = SpaceSession.Decode(loose.Snapshot);
                if (session.TryGetNetworkLooseItem(data.Guid, out _, out _)) originalLooseGuids.Add(data.Guid);
            }
        }

        public void TrackPiece(string id) => PieceIds.Add(id);

        public bool RemoveFixtureDrops()
        {
            bool removed = true;
            foreach (SpaceLooseItemState loose in new List<SpaceLooseItemState>(Session.State.LooseItems))
            {
                ItemData item = SpaceSession.Decode(loose.Snapshot);
                if (originalLooseGuids.Contains(item.Guid)) continue;
                removed &= Session.RemoveNetworkLooseItem(item.Guid);
            }
            return removed;
        }

        public void Dispose()
        {
            try
            {
                RestorePlayer();
                for (int i = PieceIds.Count - 1; i >= 0; i--)
                    if (Session.FindPiece(PieceIds[i], out ShipState ship, out ShipPieceState piece))
                        Session.RollbackPiece(ship, piece);
                RemoveFixtureDrops();
                foreach (GameObject value in PhysicsObjects) if (value != null) UnityEngine.Object.Destroy(value);
            }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void RestorePlayer()
        {
            if (Player == null) return;
            string currentWorld = GameplayWorldRegistry.GetItemWorldKey(Player);
            if (!string.IsNullOrEmpty(currentWorld) && currentWorld.StartsWith("ship:", StringComparison.Ordinal) &&
                Session.IsPlayerInShipInteriorWorld(Player, out ShipState currentShip, out _))
                Session.ReleasePassenger(Session.GetPassenger(Player), currentShip);
            if (!string.IsNullOrWhiteSpace(originalWorldKey) &&
                GameplayWorldRegistry.GetItemWorldKey(Player) != originalWorldKey)
                GameManager.AssignRegisteredPlayerAndChildItemsToWorld(Player, originalWorldKey);
            if (originalScene.IsValid() && originalScene.isLoaded && Player.gameObject.scene != originalScene)
                SceneManager.MoveGameObjectToScene(Player.gameObject, originalScene);
            Player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            if (Player.itemData != null)
            {
                Player.itemData.transform ??= new ItemTransform();
                Player.itemData.transform.position = originalDataPosition;
                Player.itemData.transform.rotation = originalDataRotation;
            }
            if (Player.Data != null) Player.Data.CurrentSceneName = originalSceneName;
            if (body != null)
            {
                body.bodyType = originalBodyType;
                body.simulated = originalBodySimulated;
                body.gravityScale = originalGravityScale;
                body.position = originalPosition;
                body.rotation = originalRotation.eulerAngles.z;
                body.velocity = originalBodyVelocity;
                body.angularVelocity = originalAngularVelocity;
            }
            Session.State.Passengers.RemoveAll(value => value.ProfileId == Player.ProfileName);
            if (hadPassenger && originalPassengerJson != null)
                Session.State.Passengers.Add(JsonConvert.DeserializeObject<SpacePassengerState>(originalPassengerJson));
        }
    }
    #endregion
}
#endif
