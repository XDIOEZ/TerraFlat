using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Spaceflight;
using FlatWorld.Structures;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class GameplayTestSpaceScenario
{
    #region 普通地面建筑装配与恢复采样
    private readonly List<ShipGroundBuildingStore.Entry> groundFuelFixtures = new();

    private bool GroundBuildingsRestored(ICollection<int> ids, string expectedWorld)
    {
        if (session.IsSpaceView || SceneManager.GetActiveScene().name != expectedWorld) return false;
        var found = new HashSet<int>();
        foreach (ShipGroundBuildingStore.Entry entry in ShipGroundBuildingStore.Collect(expectedWorld))
            if (ids.Contains(entry.Guid)) found.Add(entry.Guid);
        return found.Count == ids.Count;
    }

    private JObject GroundBuildingSnapshot(ICollection<int> ids)
    {
        string world = SceneManager.GetActiveScene().name;
        using var worldScope = MachineWorld.UseScope(world);
        List<ShipGroundBuildingStore.Entry> entries = ShipGroundBuildingStore.Collect(world);
        var selected = new JArray();
        int machineCount = 0, itemCount = 0;
        int unlimitedFuelEngineCount = 0;
        foreach (ShipGroundBuildingStore.Entry entry in entries)
        {
            if (entry.Machine != null) machineCount++; else itemCount++;
            if (!ids.Contains(entry.Guid)) continue;
            ItemData data = entry.Capture();
            Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
            Mod_Building runtimeBuilding = entry.Item?.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
            Mod_Door door = entry.Item?.itemMods?.GetMod_ByID<Mod_Door>("Door");
            ShipPartConfiguration partConfiguration = entry.Machine == null ? null : SpaceSession.ResolvePartConfiguration(data);
            bool unlimitedFuel = partConfiguration?.Kind == ShipPieceKind.Engine && partConfiguration.UnlimitedFuel;
            if (unlimitedFuel) unlimitedFuelEngineCount++;
            int placementLayer = entry.Machine?.Definition.Layer ?? BuildingOccupancyRegistry.GetPlacementLayer(runtimeBuilding);
            bool sameLayerRejected = false;
            IBuildingPlacementExtension extension = BuildingPlacementLifecycle.GetExtension(entry.Item);
            if (extension != null) sameLayerRejected = !extension.ValidatePlacement(entry.Cell, out _);
            selected.Add(new JObject
            {
                ["id"] = entry.Guid.ToString(CultureInfo.InvariantCulture), ["guid"] = entry.Guid,
                ["itemId"] = data.IDName, ["machine"] = entry.Machine != null,
                ["unlimitedFuel"] = unlimitedFuel,
                ["scope"] = entry.Machine?.ScopeKey ?? entry.Item?.gameObject.scene.name,
                ["installed"] = building?.Role == BuildingRole.PlacedBuilding &&
                    building.State is BuildingState.Installed or BuildingState.Damaged,
                ["cell"] = new JObject { ["x"] = entry.Cell.x, ["y"] = entry.Cell.y },
                ["quarterTurns"] = entry.QuarterTurns, ["snapshotGuid"] = data.Guid,
                ["placementLayer"] = placementLayer,
                ["placementOccupied"] = BuildingOccupancyRegistry.IsOccupied(entry.Cell, layer: placementLayer),
                ["sameLayerPlacementRejected"] = sameLayerRejected,
                ["specialData"] = data.ItemSpecialData,
                ["itemInstanceId"] = entry.Item != null ? entry.Item.GetInstanceID() : 0,
                ["health"] = entry.Machine?.State?.Hp ?? entry.Item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp,
                ["inventories"] = GroundMachineInventories(entry.Machine),
                ["door"] = door == null ? null : new JObject
                {
                    ["rendererBound"] = door.DoorRenderer != null && door.DoorRenderer.sprite != null,
                    ["colliderBound"] = door.DoorCollider != null,
                    ["blockingMatchesState"] = door.DoorCollider != null && door.DoorCollider.enabled == !door.Data.IsOpen,
                    ["interactable"] = door.CanInteract(null)
                }
            });
        }
        JObject fuel = GroundFuelSnapshot(ids);
        return new JObject
        {
            ["phase"] = "GroundBuildings", ["world"] = world, ["spaceView"] = session.IsSpaceView,
            ["activeShipCount"] = session.State.Ships.Count, ["totalBuildings"] = entries.Count,
            ["totalMachines"] = machineCount, ["totalItems"] = itemCount,
            ["pieceCount"] = selected.Count, ["buildings"] = selected,
            ["unlimitedFuelEngineCount"] = unlimitedFuelEngineCount,
            ["hydrogen"] = fuel["hydrogen"], ["oxygen"] = fuel["oxygen"],
            ["fluidInventories"] = fuel["inventories"],
            ["infiniteGasSources"] = fuel["infiniteGasSources"],
            ["infiniteGasSourceCount"] = fuel["infiniteGasSourceCount"]
        };
    }

    private static JArray GroundMachineInventories(MachineEntity node)
    {
        var result = new JArray();
        foreach (Inventory inventory in MachineInventoryCommands.GetMachineInventories(node))
        {
            if (inventory?.Data == null) continue;
            var slots = new JArray();
            for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            {
                ItemData item = inventory.Data.itemSlots[i].itemData;
                slots.Add(new JObject { ["index"] = i, ["itemId"] = item?.IDName,
                    ["guid"] = item?.Guid ?? 0, ["amount"] = item?.Stack?.Amount ?? 0f });
            }
            result.Add(new JObject { ["name"] = inventory.Data.Name, ["slots"] = slots });
        }
        return result;
    }

    private JObject FlightHudSnapshot()
    {
        // 正式只读接口在清船前后采同一乘员凭证，不从测试状态推断已经完成着陆。
        bool found = session.TryGetShipFlightHudSnapshot(CurrentPlayer(), out ShipFlightHudSnapshot snapshot);
        ShipLandingReceipt receipt = snapshot.LastLanding;
        return new JObject
        {
            ["found"] = found, ["driving"] = snapshot.IsDrivingSurfaceFlight,
            ["landed"] = snapshot.HasLandedSurfaceFlight, ["shipId"] = snapshot.ShipId ?? string.Empty,
            ["phase"] = snapshot.Phase.ToString(), ["height"] = snapshot.HeightMeters,
            ["simulationSeconds"] = session.State.SimulationSeconds,
            ["layout"] = CaptureVisibleShipFlightHudAndStatusRectangles(),
            ["receipt"] = new JObject
            {
                ["valid"] = receipt.IsValid, ["shipId"] = receipt.ShipId ?? string.Empty,
                ["eventId"] = receipt.EventId ?? string.Empty, ["worldKey"] = receipt.WorldKey ?? string.Empty,
                ["water"] = receipt.IsWater, ["time"] = receipt.SimulationSeconds
            }
        };
    }

    private JObject CaptureVisibleShipFlightHudAndStatusRectangles()
    {
        RectTransform safeArea = UIManager.ExistingInstance?.SafeAreaRoot;
        RectTransform hud = safeArea?.Find(PlayerShipFlightHUD.ViewName) as RectTransform;
        RectTransform status = CurrentPlayer().itemMods.GetMod_ByID<Mod_Food>(ModText.Food)?.panelUI?
            .GetComponentInChildren<UI_Drag>(true)?.rectTransform;
        bool visible = hud != null && hud.gameObject.activeInHierarchy;
        bool statusVisible = status != null && status.gameObject.activeInHierarchy;
        JObject CaptureRectangle(RectTransform rectangle)
        {
            var corners = new Vector3[4]; rectangle.GetWorldCorners(corners);
            Bounds bounds = new(safeArea.InverseTransformPoint(corners[0]), Vector3.zero);
            foreach (Vector3 corner in corners) bounds.Encapsulate(safeArea.InverseTransformPoint(corner));
            return new JObject { ["left"] = bounds.min.x, ["right"] = bounds.max.x,
                ["bottom"] = bounds.min.y, ["top"] = bounds.max.y };
        }
        return new JObject { ["visible"] = visible, ["statusVisible"] = statusVisible,
            ["hud"] = visible ? CaptureRectangle(hud) : null,
            ["status"] = visible && statusVisible ? CaptureRectangle(status) : null };
    }

    private JObject GroundViewSnapshot(ICollection<int> ids, string shipId)
    {
        JObject result = session.ShipViewCacheSnapshot();
        string worldKey = string.IsNullOrEmpty(shipId) ? null : MachineWorld.GetShipScopeKey(shipId);
        var presenters = new JArray();
        foreach (JObject row in (JArray)result["presenters"])
            if (!string.IsNullOrEmpty(shipId) && row.Value<string>("id") == shipId)
                presenters.Add(row.DeepClone());
        var items = new JArray();
        foreach (JObject row in (JArray)result["itemProjections"])
            if (int.TryParse(row.Value<string>("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int guid) &&
                ids.Contains(guid) && row.Value<string>("worldKey") == worldKey)
                items.Add(row.DeepClone());
        var chunks = new JArray();
        foreach (JObject row in (JArray)result["chunkProjections"])
            if (!string.IsNullOrEmpty(worldKey) && row.Value<string>("id")?.StartsWith(worldKey + ":", StringComparison.Ordinal) == true)
                chunks.Add(row.DeepClone());
        result["presenters"] = presenters;
        result["itemProjections"] = items;
        result["chunkProjections"] = chunks;
        result["worldKey"] = worldKey ?? string.Empty;
        result["scene"] = SceneManager.GetActiveScene().name;
        result["projectionScene"] = session.gameObject.scene.name;
        WorldPresentationTransformRegistry.TryGetSortingDomain(worldKey, out WorldPresentationSortingDomain domain);
        Transform root = domain?.Root;
        ShipState ship = string.IsNullOrEmpty(shipId) ? null : session.GetShip(shipId);
        bool hasProjection = WorldPresentationTransformRegistry.TryGet(worldKey, out Matrix4x4 projection, out bool visible);
        Vector3 origin = hasProjection ? projection.MultiplyPoint3x4(Vector3.zero) : Vector3.zero;
        Vector2 shipOrigin = ship != null ? session.ProjectShipInteriorPosition(ship, Vector2.zero) : Vector2.zero;
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.ProjectedWorldCategory, out int layer, out int order);
        var sorting = root != null ? root.GetComponent<UnityEngine.Rendering.SortingGroup>() : null;
        result["projectionOwnerId"] = root != null ? root.gameObject.GetInstanceID() : 0;
        result["projectionOwnerWorldKey"] = domain?.WorldKey ?? string.Empty;
        result["projectionDomainReady"] = root != null && root.gameObject.activeInHierarchy && visible &&
            domain.WorldKey == worldKey && root.gameObject.scene == session.gameObject.scene &&
            sorting != null && sorting.enabled && sorting.sortAtRoot && sorting.sortingLayerID == layer &&
            sorting.sortingOrder == order && hasProjection && ship != null &&
            Vector3.Distance(root.position, origin) < .002f && Vector2.Distance(origin, shipOrigin) < .002f;
        return result;
    }

    private JObject GroundFuelSnapshot(ICollection<int> ids, bool tanksOnly = false)
    {
        string world = SceneManager.GetActiveScene().name;
        decimal hydrogen = 0m, oxygen = 0m;
        var seen = new HashSet<FluidInventory>();
        var inventories = new JArray();
        var infiniteGasSources = new JArray();
        using (MachineWorld.UseScope(world))
        {
            foreach (int id in ids)
            {
                MachineEntity node = MachineWorld.GetById(id);
                if (node?.Definition.Fluid == null || node.ScopeKey != world ||
                    tanksOnly && node.Definition.Fluid.Kind != "tank") continue;
                FluidInventory inventory = MachineWorld.GetFluidInventory(node);
                if (inventory == null || !seen.Add(inventory)) continue;
                decimal h = inventory.GetGasMoles(FluidIds.Hydrogen);
                decimal o = inventory.GetGasMoles(FluidIds.Oxygen);
                hydrogen += h; oxygen += o;
                JObject infiniteSource = InfiniteGasSourceSnapshot(node, inventory);
                if (infiniteSource != null)
                    infiniteGasSources.Add(infiniteSource);
                inventories.Add(new JObject { ["guid"] = node.Id, ["scope"] = node.ScopeKey,
                    ["kind"] = node.Definition.Fluid.Kind, ["hydrogen"] = (double)h, ["oxygen"] = (double)o,
                    ["pressureKPa"] = inventory.GetPressureKPa(MachineWorld.GetFluidVolumeLiters(node),
                        MachineWorld.GetFluidMinimumGasSpaceLiters(node)),
                    ["infiniteGasSource"] = infiniteSource != null });
            }
        }
        return new JObject { ["hydrogen"] = (double)hydrogen, ["oxygen"] = (double)oxygen,
            ["inventories"] = inventories, ["infiniteGasSources"] = infiniteGasSources,
            ["infiniteGasSourceCount"] = infiniteGasSources.Count };
    }

    private static JObject InfiniteGasSourceSnapshot(MachineEntity node, FluidInventory inventory)
    {
        var composition = node?.Definition.Fluid?.InfiniteGasComposition;
        if (composition == null || composition.Count == 0 || inventory?.HasInfiniteGasSupply != true) return null;
        return new JObject
        {
            ["guid"] = node.Id, ["infiniteGasSource"] = true,
            ["composition"] = JObject.FromObject(composition),
            ["hydrogen"] = (double)inventory.GetGasMoles(FluidIds.Hydrogen),
            ["oxygen"] = (double)inventory.GetGasMoles(FluidIds.Oxygen),
            ["totalMoles"] = (double)inventory.GasMoles
        };
    }
    #endregion

    #region 落地后的正式普通管路自动加油
    private bool TemplateShipPresentationReady(ShipState ship)
    {
        string scene = SceneManager.GetActiveScene().name;
        foreach (JObject row in (JArray)session.ShipViewCacheSnapshot()["presenters"])
            if (row.Value<string>("id") == ship.ShipId && row.Value<bool>("active") &&
                !row.Value<bool>("pooled") && row.Value<string>("scene") == scene) return true;
        return false;
    }

    private IEnumerator LandTemplateAndRefuel(ShipState ship, string console, HashSet<int> ids,
        string expectedWorld, Action<JObject> completed, bool inspectPreparation = false)
    {
        session.ClearControl(console);
        // 高度尺与落地回执覆盖正式驾驶员流程，不能在提前离开驾驶位后采样。
        if (inspectPreparation && !session.EnterConsole(CurrentPlayer(), console))
            throw new InvalidOperationException("落地专项无法恢复真实驾驶位。");
        Dictionary<int, GroundMachinePreparationBaseline> machineBaselines = inspectPreparation ? CaptureGroundPreparationMachines(ship) : null;
        bool accepted = session.RequestLanding(console);
        if (!accepted) throw new InvalidOperationException("飞船已离开可降落范围，正式降落入口拒绝了请求。");
        IsMigrationExpected = true;
        yield return WaitUntil(() => Ready() && session.GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.SurfaceDescending &&
                TemplateShipPresentationReady(ship),
            90d, "正式降落请求没有进入地表下降阶段。");
        if (inspectPreparation) yield return new WaitForEndOfFrame();
        JObject descending = ShipSnapshot(ship), descendingViews = ShipViewSnapshot(ship);
        descending["flightHud"] = FlightHudSnapshot();
        JObject preparation = null, latestBeforeCommit = null;
        if (inspectPreparation)
        {
            yield return ExerciseGroundPreparation(ship, console, ids, expectedWorld, machineBaselines, value => preparation = value);
            latestBeforeCommit = ShipSnapshot(ship);
        }
        yield return WaitUntil(() =>
            {
                if (!Ready()) return false;
                if (session.GetShip(ship.ShipId) != null)
                { if (inspectPreparation) latestBeforeCommit = ShipSnapshot(ship); return false; }
                return GroundBuildingsRestored(ids, expectedWorld);
            },
            90d, "飞船未完成正式降落及普通地面建筑移交。", () =>
                new JObject
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["expectedWorld"] = expectedWorld,
                    ["worldReady"] = Ready(),
                    ["spaceView"] = session.IsSpaceView,
                    ["shipPresent"] = session.GetShip(ship.ShipId) != null,
                    ["buildingsRestored"] = GroundBuildingsRestored(ids, expectedWorld),
                    ["flightPhase"] = session.GetFlight(ship.ShipId)?.Phase.ToString(),
                    ["ship"] = ShipSnapshot(ship)
                }.ToString(Newtonsoft.Json.Formatting.None));
        IsMigrationExpected = false;
        JObject landed = GroundBuildingSnapshot(ids);
        CaptureGroundOrdinaryItemStage(ids, expectedWorld, "surface-after-landing");
        landed["flightRemoved"] = session.GetFlight(ship.ShipId) == null;
        landed["shipRemoved"] = session.GetShip(ship.ShipId) == null;
        landed["supported"] = session.GetPassenger(CurrentPlayer()).Supported;
        landed["flightHud"] = FlightHudSnapshot();
        if (preparation != null)
        {
            preparation["latestBeforeCommit"] = latestBeforeCommit;
            preparation["committed"] = session.GroundPreparationSnapshot(ship.ShipId);
            preparation["landedMachines"] = GroundPreparationMachineReadings(ship, machineBaselines, expectedWorld);
        }
        JObject returnedViews = ShipViewSnapshot(ship), refuel = null;
        yield return RefuelLandedBuildings(ids, value => refuel = value);
        completed(new JObject { ["landingAccepted"] = accepted, ["descending"] = descending,
            ["descendingViews"] = descendingViews, ["landedAgain"] = landed,
            ["returnedViews"] = returnedViews, ["autoRefuel"] = refuel, ["preparation"] = preparation,
            ["ordinaryItemStages"] = JArray.FromObject(groundOrdinaryItemStages) });
    }

    private IEnumerator RefuelLandedBuildings(HashSet<int> shipIds, Action<JObject> completed)
    {
        string world = SceneManager.GetActiveScene().name;
        MachineEntity endpoint = null;
        Vector2Int targetCell = default, pipeCell = default, sourceCell = default, outletCell = default;
        int outletQuarterTurns = 0;
        bool targetIsFixture = false;
        MachineDefinition pipeDefinition = MachineCatalog.Get("FluidPipe");
        MachineDefinition sourceDefinition = MachineCatalog.Get("GasTankBlock_Steel");
        MachineDefinition outletDefinition = MachineCatalog.Get("FluidPipe");
        if (pipeDefinition?.Fluid == null || sourceDefinition?.Fluid == null || outletDefinition?.Fluid == null)
            throw new InvalidOperationException("普通自动加油夹具缺少正式管道、出气口或源气罐定义。");
        var nodes = new List<MachineEntity>();
        Vector2Int[] directions = MechanicalNetworkGraph.Directions;
        var fluidCells = new HashSet<Vector2Int>();
        bool TryFindRoute(Vector2Int target, bool targetExists)
        {
            if (!targetExists && (BuildingOccupancyRegistry.IsOccupied(target, layer: sourceDefinition.Layer) ||
                fluidCells.Contains(target))) return false;
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                Vector2Int direction = directions[directionIndex];
                Vector2Int outlet = WorldTopologyRuntime.NormalizeCell(target + direction);
                Vector2Int pipe = WorldTopologyRuntime.NormalizeCell(outlet + direction);
                Vector2Int source = WorldTopologyRuntime.NormalizeCell(pipe + direction);
                if (fluidCells.Contains(outlet) || fluidCells.Contains(pipe) || fluidCells.Contains(source) ||
                    BuildingOccupancyRegistry.IsOccupied(outlet, layer: outletDefinition.Layer) ||
                    BuildingOccupancyRegistry.IsOccupied(pipe, layer: pipeDefinition.Layer) ||
                    BuildingOccupancyRegistry.IsOccupied(source, layer: sourceDefinition.Layer)) continue;
                bool isolated = true;
                foreach (Vector2Int neighbor in directions)
                {
                    Vector2Int besideOutlet = WorldTopologyRuntime.NormalizeCell(outlet + neighbor);
                    Vector2Int besidePipe = WorldTopologyRuntime.NormalizeCell(pipe + neighbor);
                    Vector2Int besideSource = WorldTopologyRuntime.NormalizeCell(source + neighbor);
                    if (besideOutlet != target && fluidCells.Contains(besideOutlet) ||
                        fluidCells.Contains(besidePipe) || fluidCells.Contains(besideSource))
                    { isolated = false; break; }
                }
                if (!isolated) continue;
                targetCell = target; outletCell = outlet; pipeCell = pipe; sourceCell = source;
                outletQuarterTurns = (directionIndex + 2) & 3;
                return true;
            }
            return false;
        }
        using (MachineWorld.UseScope(world))
        {
            MachineWorld.CollectNodes(nodes);
            foreach (MachineEntity node in nodes)
                if (node.Definition.Fluid != null) fluidCells.Add(node.Cell);
            foreach (MachineEntity node in nodes)
            {
                FluidMachineDefinition fluid = node.Definition.Fluid;
                FluidInventory inventory = fluid == null ? null : MachineWorld.GetFluidInventory(node);
                // 无限气源的展示样本不能作为落地管路接收端。
                if (!shipIds.Contains(node.Id) || node.Definition.Fluid?.Kind != "tank" ||
                    fluid.GasCapacityStandardLiters <= 0d || fluid.InfiniteGasComposition?.Count > 0 ||
                    inventory?.HasInfiniteGasSupply == true || MachineWorld.GetIndustrialFreeCapacity(node, FluidPhase.Gas) <= 0d) continue;
                if (TryFindRoute(node.Cell, targetExists: true))
                {
                    endpoint = node;
                    break;
                }
            }
        }
        if (endpoint == null)
        {
            bool foundCell = false;
            foreach (ShipGroundBuildingStore.Entry member in ShipGroundBuildingStore.Collect(world))
            {
                if (!shipIds.Contains(member.Guid)) continue;
                for (int distance = 1; distance <= 8 && !foundCell; distance++)
                    foreach (Vector2Int direction in directions)
                    {
                        Vector2Int candidate = WorldTopologyRuntime.NormalizeCell(member.Cell + direction * distance);
                        if (TryFindRoute(candidate, targetExists: false)) { foundCell = true; break; }
                    }
                if (foundCell) break;
            }
            if (!foundCell) throw new InvalidOperationException("落地现场没有可安全放置普通有限接收罐和管路夹具的位置。");
            ItemData receiverData = GameRes.ExistingInstance.CreateItemData("GasTankBlock_Steel");
            ShipGroundBuildingStore.Entry receiverEntry = ShipGroundBuildingStore.Place(receiverData, targetCell, 0, world);
            groundFuelFixtures.Add(receiverEntry);
            endpoint = receiverEntry.Machine ?? throw new InvalidOperationException("普通有限接收罐没有恢复为机器节点。");
            targetIsFixture = true;
        }
        var receiverIds = new HashSet<int> { endpoint.Id };
        ItemData sourceData = GameRes.ExistingInstance.CreateItemData("GasTankBlock_Steel");
        FixedStructureResourceInitializer.Prepare(sourceData, new FixedStructureResources
        {
            Gas = new FixedStructureGas { StandardLiters = sourceDefinition.Fluid.GasCapacityStandardLiters, TemperatureCelsius = 20d,
                Components = new Dictionary<string, double> { [FluidIds.Hydrogen] = 2d, [FluidIds.Oxygen] = 1d } }
        });
        // 仅给独立加油站源罐准备燃料，落地船罐的增量必须来自正式世界管道固定步。
        ShipGroundBuildingStore.Entry sourceEntry = ShipGroundBuildingStore.Place(sourceData, sourceCell, 0, world);
        groundFuelFixtures.Add(sourceEntry);
        var sourceIds = new HashSet<int> { sourceEntry.Guid };
        var allIds = new HashSet<int>(shipIds) { sourceEntry.Guid };
        allIds.Add(endpoint.Id);
        ShipGroundBuildingStore.Entry pipeEntry = ShipGroundBuildingStore.Place(
            GameRes.ExistingInstance.CreateItemData("FluidPipe"), pipeCell, 0, world);
        groundFuelFixtures.Add(pipeEntry); allIds.Add(pipeEntry.Guid);
        ShipGroundBuildingStore.Entry outletEntry = ShipGroundBuildingStore.Place(
            GameRes.ExistingInstance.CreateItemData("FluidPipe"), outletCell, outletQuarterTurns, world);
        groundFuelFixtures.Add(outletEntry); allIds.Add(outletEntry.Guid);
        JObject targetsBefore = GroundFuelSnapshot(receiverIds, tanksOnly: true);
        JObject sourceBefore = GroundFuelSnapshot(sourceIds);
        JObject totalBefore = GroundFuelSnapshot(allIds);
        bool targetIsFiniteWritableTank;
        using (MachineWorld.UseScope(world))
        {
            FluidInventory targetInventory = MachineWorld.GetFluidInventory(endpoint);
            targetIsFiniteWritableTank = endpoint.Definition.Fluid.Kind == "tank" &&
                (endpoint.Definition.Fluid.InfiniteGasComposition == null || endpoint.Definition.Fluid.InfiniteGasComposition.Count == 0) &&
                targetInventory?.HasInfiniteGasSupply != true &&
                MachineWorld.GetIndustrialFreeCapacity(endpoint, FluidPhase.Gas) > 0d;
        }
        double beforeTime = session.State.SimulationSeconds;
        yield return WaitUntil(() => session.State.SimulationSeconds >= beforeTime + .2d,
            15d, "独立罐边界检查未推进正式固定步。");
        completed(new JObject
        {
            ["fixtureSetup"] = "landed-independent-finite-tanks-no-passive-transfer", ["world"] = world,
            ["sourceGuid"] = sourceEntry.Guid, ["pipeGuid"] = pipeEntry.Guid, ["targetGuid"] = endpoint.Id,
            ["targetIsFixture"] = targetIsFixture, ["receiverIds"] = JArray.FromObject(receiverIds),
            ["outletGuid"] = outletEntry.Guid, ["outletQuarterTurns"] = outletQuarterTurns,
            ["sourceScope"] = sourceEntry.Machine.ScopeKey, ["pipeScope"] = pipeEntry.Machine.ScopeKey,
            ["outletScope"] = outletEntry.Machine.ScopeKey,
            ["targetScope"] = endpoint.ScopeKey,
            ["targetIsGasTank"] = endpoint.Definition.Fluid.Kind == "tank" && endpoint.Definition.Fluid.GasCapacityStandardLiters > 0d,
            ["targetIsFiniteWritableTank"] = targetIsFiniteWritableTank,
            ["pipeMask"] = MachineWorld.GetFluidPipeConnectionMask(pipeEntry.Machine),
            ["targetsBefore"] = targetsBefore, ["targetsAfter"] = GroundFuelSnapshot(receiverIds, true),
            ["sourceBefore"] = sourceBefore, ["sourceAfter"] = GroundFuelSnapshot(sourceIds),
            ["totalBefore"] = totalBefore, ["totalAfter"] = GroundFuelSnapshot(allIds),
            ["simulationSeconds"] = session.State.SimulationSeconds - beforeTime,
            ["targetDirectRefill"] = false,
            ["limitations"] = "落地后连接独立源罐和无库存管道，验证没有消费端时不自动搬料；不代表有动力加油站或多人客户端通过。"
        });
    }

    private void CleanupGroundFuelFixtures()
    {
        var failures = new List<Exception>();
        for (int i = groundFuelFixtures.Count - 1; i >= 0; i--)
            try { ShipGroundBuildingStore.RollbackCreated(groundFuelFixtures[i]); }
            catch (Exception error) { failures.Add(error); }
        groundFuelFixtures.Clear();
        if (failures.Count > 0) throw new AggregateException("普通地面加油站夹具清理失败。", failures);
    }
    #endregion
}
