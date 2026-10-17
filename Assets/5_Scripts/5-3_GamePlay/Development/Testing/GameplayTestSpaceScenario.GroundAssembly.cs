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
        List<ShipGroundBuildingStore.Entry> entries = ShipGroundBuildingStore.Collect(world);
        var selected = new JArray();
        int machineCount = 0, itemCount = 0;
        foreach (ShipGroundBuildingStore.Entry entry in entries)
        {
            if (entry.Machine != null) machineCount++; else itemCount++;
            if (!ids.Contains(entry.Guid)) continue;
            ItemData data = entry.Capture();
            Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
            selected.Add(new JObject
            {
                ["id"] = entry.Guid.ToString(CultureInfo.InvariantCulture), ["guid"] = entry.Guid,
                ["itemId"] = data.IDName, ["machine"] = entry.Machine != null,
                ["scope"] = entry.Machine?.ScopeKey ?? entry.Item?.gameObject.scene.name,
                ["installed"] = building?.Role == BuildingRole.PlacedBuilding &&
                    building.State is BuildingState.Installed or BuildingState.Damaged,
                ["cell"] = new JObject { ["x"] = entry.Cell.x, ["y"] = entry.Cell.y },
                ["quarterTurns"] = entry.QuarterTurns, ["snapshotGuid"] = data.Guid,
                ["specialData"] = data.ItemSpecialData,
                ["itemInstanceId"] = entry.Item != null ? entry.Item.GetInstanceID() : 0,
                ["health"] = entry.Machine?.State?.Hp ?? entry.Item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp,
                ["inventories"] = GroundMachineInventories(entry.Machine)
            });
        }
        JObject fuel = GroundFuelSnapshot(ids, tanksOnly: true);
        return new JObject
        {
            ["phase"] = "GroundBuildings", ["world"] = world, ["spaceView"] = session.IsSpaceView,
            ["activeShipCount"] = session.State.Ships.Count, ["totalBuildings"] = entries.Count,
            ["totalMachines"] = machineCount, ["totalItems"] = itemCount,
            ["pieceCount"] = selected.Count, ["buildings"] = selected,
            ["hydrogen"] = fuel["hydrogen"], ["oxygen"] = fuel["oxygen"],
            ["fluidInventories"] = fuel["inventories"]
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
            ["receipt"] = new JObject
            {
                ["valid"] = receipt.IsValid, ["shipId"] = receipt.ShipId ?? string.Empty,
                ["eventId"] = receipt.EventId ?? string.Empty, ["worldKey"] = receipt.WorldKey ?? string.Empty,
                ["water"] = receipt.IsWater, ["time"] = receipt.SimulationSeconds
            }
        };
    }

    private JObject GroundViewSnapshot(ICollection<int> ids, string shipId)
    {
        JObject result = session.ShipViewCacheSnapshot();
        var pieces = new JArray();
        var parentIds = new HashSet<int>();
        foreach (JObject row in (JArray)result["pieces"])
            if (int.TryParse(row.Value<string>("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && ids.Contains(id))
            { pieces.Add(row.DeepClone()); parentIds.Add(row.Value<int>("parentId")); }
        var presenters = new JArray();
        foreach (JObject row in (JArray)result["presenters"])
            if (row.Value<string>("id") == shipId || parentIds.Contains(row.Value<int>("instanceId")))
                presenters.Add(row.DeepClone());
        int parkedPieces = 0;
        foreach (Transform value in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            if (value.name == "ParkedPieces") parkedPieces++;
        result["pieces"] = pieces;
        result["presenters"] = presenters;
        result["parkedPieces"] = parkedPieces;
        result["scene"] = SceneManager.GetActiveScene().name;
        return result;
    }

    private JObject GroundFuelSnapshot(ICollection<int> ids, bool tanksOnly = false)
    {
        string world = SceneManager.GetActiveScene().name;
        decimal hydrogen = 0m, oxygen = 0m;
        var seen = new HashSet<FluidInventory>();
        var inventories = new JArray();
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
                inventories.Add(new JObject { ["guid"] = node.Id, ["scope"] = node.ScopeKey,
                    ["kind"] = node.Definition.Fluid.Kind, ["hydrogen"] = (double)h, ["oxygen"] = (double)o,
                    ["pressureKPa"] = inventory.GetPressureKPa(MachineWorld.GetFluidVolumeLiters(node),
                        MachineWorld.GetFluidMinimumGasSpaceLiters(node)) });
            }
        }
        return new JObject { ["hydrogen"] = (double)hydrogen, ["oxygen"] = (double)oxygen, ["inventories"] = inventories };
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
        Dictionary<int, GroundMachinePreparationBaseline> machineBaselines = inspectPreparation ? CaptureGroundPreparationMachines(ship) : null;
        bool accepted = session.RequestLanding(console);
        if (!accepted) throw new InvalidOperationException("飞船已离开可降落范围，正式降落入口拒绝了请求。");
        IsMigrationExpected = true;
        yield return WaitUntil(() => Ready() && session.GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.SurfaceDescending &&
                TemplateShipPresentationReady(ship),
            90d, "正式降落请求没有进入地表下降阶段。");
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
            90d, "飞船未完成正式降落及普通地面建筑移交。");
        IsMigrationExpected = false;
        JObject landed = GroundBuildingSnapshot(ids);
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
            ["returnedViews"] = returnedViews, ["autoRefuel"] = refuel, ["preparation"] = preparation });
    }

    private IEnumerator RefuelLandedBuildings(HashSet<int> shipIds, Action<JObject> completed)
    {
        string world = SceneManager.GetActiveScene().name;
        MachineEntity endpoint = null;
        Vector2Int pipeCell = default, sourceCell = default, outletCell = default;
        int outletQuarterTurns = 0;
        MachineDefinition pipeDefinition = MachineCatalog.Get("FluidPipe");
        MachineDefinition sourceDefinition = MachineCatalog.Get("GasTankBlock_Steel");
        MachineDefinition outletDefinition = MachineCatalog.Get("FluidOutlet");
        if (pipeDefinition?.Fluid == null || sourceDefinition?.Fluid == null || outletDefinition?.Fluid == null)
            throw new InvalidOperationException("普通自动加油夹具缺少正式管道、出气口或源气罐定义。");
        var nodes = new List<MachineEntity>();
        Vector2Int[] directions = MechanicalNetworkGraph.Directions;
        using (MachineWorld.UseScope(world))
        {
            MachineWorld.CollectNodes(nodes);
            var fluidCells = new HashSet<Vector2Int>();
            foreach (MachineEntity node in nodes)
                if (node.Definition.Fluid != null) fluidCells.Add(node.Cell);
            foreach (MachineEntity node in nodes)
            {
                if (!shipIds.Contains(node.Id) || node.Definition.Fluid?.Kind != "tank") continue;
                FluidInventory inventory = MachineWorld.GetFluidInventory(node);
                if (inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas) <= 0m ||
                    inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas) <= 0m) continue;
                for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
                {
                    Vector2Int direction = directions[directionIndex];
                    Vector2Int outlet = WorldTopologyRuntime.NormalizeCell(node.Cell + direction);
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
                        if (besideOutlet != node.Cell && fluidCells.Contains(besideOutlet) ||
                            fluidCells.Contains(besidePipe) || fluidCells.Contains(besideSource))
                        { isolated = false; break; }
                    }
                    if (!isolated) continue;
                    endpoint = node; pipeCell = pipe; sourceCell = source; outletCell = outlet;
                    outletQuarterTurns = (directionIndex + 2) & 3; break;
                }
                if (endpoint != null) break;
            }
        }
        if (endpoint == null) throw new InvalidOperationException("落地燃料网络没有可安全邻接的普通管道接入口。");
        double safePressure = Math.Min(pipeDefinition.Fluid.MaxSafePressureKPa,
            Math.Min(outletDefinition.Fluid.MaxSafePressureKPa,
                Math.Min(sourceDefinition.Fluid.MaxSafePressureKPa, endpoint.Definition.Fluid.MaxSafePressureKPa))) * .9d;
        ItemData sourceData = GameRes.ExistingInstance.CreateItemData("GasTankBlock_Steel");
        FixedStructureResourceInitializer.Prepare(sourceData, new FixedStructureResources
        {
            Gas = new FixedStructureGas { PressureKPa = safePressure, TemperatureCelsius = 20d,
                Components = new Dictionary<string, double> { [FluidIds.Hydrogen] = 2d, [FluidIds.Oxygen] = 1d } }
        });
        // 仅给独立加油站源罐准备燃料，落地船罐的增量必须来自正式世界管道固定步。
        ShipGroundBuildingStore.Entry sourceEntry = ShipGroundBuildingStore.Place(sourceData, sourceCell, 0, world);
        groundFuelFixtures.Add(sourceEntry);
        var sourceIds = new HashSet<int> { sourceEntry.Guid };
        var allIds = new HashSet<int>(shipIds) { sourceEntry.Guid };
        JObject targetsBefore = GroundFuelSnapshot(shipIds, tanksOnly: true);
        JObject sourceBefore = GroundFuelSnapshot(sourceIds);
        ShipGroundBuildingStore.Entry pipeEntry = ShipGroundBuildingStore.Place(
            GameRes.ExistingInstance.CreateItemData("FluidPipe"), pipeCell, 0, world);
        groundFuelFixtures.Add(pipeEntry); allIds.Add(pipeEntry.Guid);
        ShipGroundBuildingStore.Entry outletEntry = ShipGroundBuildingStore.Place(
            GameRes.ExistingInstance.CreateItemData("FluidOutlet"), outletCell, outletQuarterTurns, world);
        groundFuelFixtures.Add(outletEntry); allIds.Add(outletEntry.Guid);
        JObject totalBefore = GroundFuelSnapshot(allIds);
        double beforeTime = session.State.SimulationSeconds;
        yield return WaitUntil(() =>
        {
            JObject targets = GroundFuelSnapshot(shipIds, tanksOnly: true);
            return targets.Value<double>("hydrogen") > targetsBefore.Value<double>("hydrogen") + .000001d &&
                targets.Value<double>("oxygen") > targetsBefore.Value<double>("oxygen") + .000001d;
        }, 15d, "落地普通燃料罐没有通过邻接普通管道自动加油。",
            () => new JObject { ["targetBefore"] = targetsBefore, ["targetNow"] = GroundFuelSnapshot(shipIds, true),
                ["sourceNow"] = GroundFuelSnapshot(sourceIds),
                ["pipeMask"] = MachineWorld.GetFluidPipeConnectionMask(pipeEntry.Machine) }.ToString());
        completed(new JObject
        {
            ["fixtureSetup"] = "landed-world-pipe-outlet-source-tank", ["world"] = world,
            ["sourceGuid"] = sourceEntry.Guid, ["pipeGuid"] = pipeEntry.Guid, ["targetGuid"] = endpoint.Id,
            ["outletGuid"] = outletEntry.Guid, ["outletQuarterTurns"] = outletQuarterTurns,
            ["sourceScope"] = sourceEntry.Machine.ScopeKey, ["pipeScope"] = pipeEntry.Machine.ScopeKey,
            ["outletScope"] = outletEntry.Machine.ScopeKey,
            ["targetScope"] = endpoint.ScopeKey,
            ["pipeMask"] = MachineWorld.GetFluidPipeConnectionMask(pipeEntry.Machine),
            ["targetsBefore"] = targetsBefore, ["targetsAfter"] = GroundFuelSnapshot(shipIds, true),
            ["sourceBefore"] = sourceBefore, ["sourceAfter"] = GroundFuelSnapshot(sourceIds),
            ["totalBefore"] = totalBefore, ["totalAfter"] = GroundFuelSnapshot(allIds),
            ["simulationSeconds"] = session.State.SimulationSeconds - beforeTime,
            ["targetDirectRefill"] = false,
            ["limitations"] = "真实落地后连接普通加油站管路并等待正式世界输送；未覆盖预建加油站的精确驾驶落点或多人客户端。"
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
