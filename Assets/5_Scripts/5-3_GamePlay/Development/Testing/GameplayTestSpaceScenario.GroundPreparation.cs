using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class GameplayTestSpaceScenario
{
    #region 降落准备的正式流程采样

    private sealed class GroundMachinePreparationBaseline
    {
        public MachineEntity Node;
        public RecipeProcessor Processor;
        public readonly List<Inventory> Inventories = new();
        public FluidInventory Fluid;
    }

    private Dictionary<int, GroundMachinePreparationBaseline> CaptureGroundPreparationMachines(ShipState ship)
    {
        var result = new Dictionary<int, GroundMachinePreparationBaseline>();
        using var scope = MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId));
        foreach (ShipPieceState piece in ship.Pieces)
        {
            if (!piece.IsAlive || MachineCatalog.Get(piece.ItemId) == null) continue;
            int id = int.Parse(piece.PieceId, CultureInfo.InvariantCulture);
            MachineEntity node = MachineWorld.GetById(id) ?? throw new InvalidOperationException("下降前机器身份缺失：" + id);
            var baseline = new GroundMachinePreparationBaseline { Node = node, Processor = node.Processor,
                Fluid = node.Definition.Fluid != null ? MachineWorld.GetFluidInventory(node) : null };
            baseline.Inventories.AddRange(MachineInventoryCommands.GetMachineInventories(node));
            result.Add(id, baseline);
        }
        return result;
    }

    private JArray GroundPreparationMachineReadings(ShipState ship,
        Dictionary<int, GroundMachinePreparationBaseline> baselines, string world)
    {
        var result = new JArray();
        foreach (var pair in baselines)
        {
            MachineEntity source, destination;
            using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId))) source = MachineWorld.GetById(pair.Key);
            using (MachineWorld.UseScope(world)) destination = MachineWorld.GetById(pair.Key);
            // GetById 会按全局 Guid 回退查询，归属判断还必须核对节点真实作用域。
            if (source?.ScopeKey != MachineWorld.GetShipScopeKey(ship.ShipId)) source = null;
            if (destination?.ScopeKey != world) destination = null;
            MachineEntity current = source ?? destination;
            var baseline = pair.Value;
            bool inventoriesSame = current != null;
            IReadOnlyList<Inventory> inventories = MachineInventoryCommands.GetMachineInventories(current);
            inventoriesSame &= inventories.Count == baseline.Inventories.Count;
            for (int i = 0; inventoriesSame && i < inventories.Count; i++)
                inventoriesSame &= ReferenceEquals(inventories[i], baseline.Inventories[i]);
            FluidInventory fluid = null;
            if (current?.Definition.Fluid != null)
                using (MachineWorld.UseNodeScope(current)) fluid = MachineWorld.GetFluidInventory(current);
            result.Add(new JObject
            {
                ["guid"] = pair.Key, ["scope"] = current?.ScopeKey,
                ["shipRegistered"] = source != null, ["surfaceRegistered"] = destination != null,
                ["sameMachine"] = ReferenceEquals(current, baseline.Node),
                ["sameProcessor"] = ReferenceEquals(current?.Processor, baseline.Processor),
                ["sameInventories"] = inventoriesSame, ["inventoryCount"] = inventories.Count,
                ["fluid"] = baseline.Node.Definition.Fluid != null,
                ["sameFluidWrapper"] = ReferenceEquals(fluid, baseline.Fluid)
            });
        }
        return result;
    }

    private JObject GroundPreparationLiveSnapshot(ShipState ship, HashSet<int> ids,
        Dictionary<int, GroundMachinePreparationBaseline> baselines, string world)
    {
        JObject result = session.GroundPreparationSnapshot(ship.ShipId);
        int groundMatches = 0, registeredOriginalItems = 0;
        foreach (ShipGroundBuildingStore.Entry entry in ShipGroundBuildingStore.Collect(world))
            if (ids.Contains(entry.Guid)) groundMatches++;
        foreach (int id in ids)
            if (ItemMgr.Instance.WorldRunTimeItems.ContainsKey(id)) registeredOriginalItems++;
        ShipFlightState flight = session.GetFlight(ship.ShipId);
        result["shipPresent"] = ReferenceEquals(session.GetShip(ship.ShipId), ship);
        result["phase"] = flight?.Phase.ToString(); result["height"] = flight?.HeightMeters;
        result["position"] = Vector(new SpaceVector2(ship.PositionX, ship.PositionY));
        result["groundMatches"] = groundMatches; result["registeredOriginalItems"] = registeredOriginalItems;
        result["machines"] = GroundPreparationMachineReadings(ship, baselines, world);
        return result;
    }

    private IEnumerator WaitForPreparedGroundShip(ShipState ship)
    {
        yield return WaitUntil(() =>
        {
            JObject state = session.GroundPreparationSnapshot(ship.ShipId);
            if (state.Value<string>("state") == "failed")
                throw new InvalidOperationException("落地候选准备失败：" + state.Value<string>("error"));
            return Ready() && session.GetShip(ship.ShipId) != null && state.Value<bool>("ready");
        }, 45d, "真实地表下降没有备齐隐藏建筑与机器表现。",
            () => new JObject
            {
                ["preparation"] = session.GroundPreparationSnapshot(ship.ShipId),
                ["sessionEnabled"] = session.enabled, ["sessionActive"] = session.gameObject.activeInHierarchy,
                ["shipPresent"] = session.GetShip(ship.ShipId) != null,
                ["phase"] = session.GetFlight(ship.ShipId)?.Phase.ToString(),
                ["timeScale"] = Time.timeScale, ["worldReady"] = Ready(), ["frame"] = Time.frameCount
            }.ToString());
    }

    private JObject CancelOrdinaryGroundCandidate(ShipState ship)
    {
        ShipPieceState floor = FindGroundPreparationFloor(ship);
        ItemData source = GameRes.ExistingInstance.CreateItemData(floor.ItemId);
        int sourceGuid = source.Guid;
        using ItemMgr.PreparedItemHandle candidate = ItemMgr.Instance.PrepareItem(source);
        int instanceId = candidate.InstanceId;
        Item prepared = null;
        foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
            if (item.GetInstanceID() == instanceId) { prepared = item; break; }
        var before = new JObject { ["valid"] = candidate.IsValid, ["active"] = candidate.IsActive,
            ["registered"] = candidate.IsRegistered, ["guid"] = candidate.Guid, ["instanceId"] = instanceId,
            ["found"] = prepared != null };
        candidate.Dispose();
        candidate.Dispose();
        return new JObject { ["before"] = before, ["validAfter"] = candidate.IsValid,
            ["instanceAfter"] = candidate.InstanceId, ["sourceGuidBefore"] = sourceGuid, ["sourceGuidAfter"] = source.Guid,
            ["retired"] = prepared == null || prepared.IsInPool || prepared.DestructionHandled,
            ["activeAfter"] = prepared != null && prepared.gameObject.activeInHierarchy,
            ["registeredAfter"] = prepared != null && ItemMgr.Instance.IsRuntimeItemRegistered(prepared) };
    }

    private static ShipPieceState FindGroundPreparationFloor(ShipState ship)
    {
        foreach (ShipPieceState piece in ship.Pieces)
            if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor && piece.Width == 1 && piece.Height == 1 && piece.Health > 2d)
                return piece;
        throw new InvalidOperationException("测试船没有可单独取样的健康一格地板。");
    }

    private static JArray GroundCandidateRetirementReadings(JObject previous, JObject current)
    {
        var previousIds = new HashSet<int>();
        var currentIds = new HashSet<int>();
        foreach (JObject row in (JArray)previous["candidates"]) previousIds.Add(row.Value<int>("instanceId"));
        foreach (JObject row in (JArray)current["candidates"]) currentIds.Add(row.Value<int>("instanceId"));
        var found = new Dictionary<int, Item>();
        foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
            if (previousIds.Contains(item.GetInstanceID())) found.Add(item.GetInstanceID(), item);
        var result = new JArray();
        foreach (int id in previousIds)
        {
            found.TryGetValue(id, out Item item);
            result.Add(new JObject { ["instanceId"] = id, ["currentCandidate"] = currentIds.Contains(id),
                ["retired"] = item == null || item.IsInPool || item.DestructionHandled,
                ["active"] = item != null && item.gameObject.activeInHierarchy,
                ["registered"] = item != null && ItemMgr.Instance.IsRuntimeItemRegistered(item) });
        }
        return result;
    }

    private ShipPieceState AddGroundPreparationFloor(ShipState ship)
    {
        ShipPieceState floor = FindGroundPreparationFloor(ship);
        var occupied = new HashSet<ShipCell>();
        foreach (ShipPieceState piece in ship.Pieces)
            if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor)
                foreach (ShipCell cell in ShipGeometry.Footprint(piece)) occupied.Add(cell);
        foreach (ShipCell own in occupied)
        foreach (ShipCell cell in ShipGeometry.Neighbors(own))
        {
            if (occupied.Contains(cell)) continue;
            var proposal = new ShipPieceState { Kind = ShipPieceKind.Floor, CellX = cell.X, CellY = cell.Y,
                Width = 1, Height = 1, Health = 100d };
            if (!ShipEnginePlacement.TryValidateAddition(ship, proposal, out _)) continue;
            ItemData data = GameRes.ExistingInstance.CreateItemData(floor.ItemId);
            Mod_Building.SetInstalledDataState(data);
            Vector2 position = session.DisplayPoint(ship, (cell.X + .5d) * ship.CellSizeMeters, (cell.Y + .5d) * ship.CellSizeMeters);
            position.y += (float)ShipSurfaceState.VisualRise(session.GetFlight(ship.ShipId));
            ShipPieceState added = session.AddPiece(data, position, 0, out ShipState owner);
            contactFixtures.Add((owner, added));
            if (!ReferenceEquals(owner, ship))
                throw new InvalidOperationException("外缘地板没有经正式放置接口接入原船。");
            session.CommitPiece(owner, added);
            return added;
        }
        throw new InvalidOperationException("测试船没有不遮挡正式喷口的空外缘邻格。");
    }

    private IEnumerator ExerciseGroundPreparation(ShipState ship, string console, HashSet<int> ids, string world,
        Dictionary<int, GroundMachinePreparationBaseline> baselines, Action<JObject> completed)
    {
        JObject cancellation = CancelOrdinaryGroundCandidate(ship);
        yield return WaitForPreparedGroundShip(ship);
        JObject ready = GroundPreparationLiveSnapshot(ship, ids, baselines, world);
        session.SetControl(console, Vector2.right * .1f, 0f);
        try { yield return Wait(.08d); }
        finally { session.ClearControl(console); }
        yield return Wait(.08d);
        JObject moved = GroundPreparationLiveSnapshot(ship, ids, baselines, world);
        ShipPieceState added = AddGroundPreparationFloor(ship);
        string addedId = added.PieceId;
        JObject expanded;
        try
        {
            yield return WaitUntil(() => session.GroundPreparationSnapshot(ship.ShipId).Value<int>("invalidations") >
                    moved.Value<int>("invalidations"), 15d, "正式加装地板没有使旧准备布局失效。");
            yield return WaitForPreparedGroundShip(ship);
            expanded = GroundPreparationLiveSnapshot(ship, ids, baselines, world);
            expanded["previousCandidates"] = GroundCandidateRetirementReadings(moved, expanded);
        }
        finally
        {
            if (session.GetShip(ship.ShipId) != null && ship.Pieces.Contains(added)) session.RollbackPiece(ship, added);
            contactFixtures.RemoveAll(value => ReferenceEquals(value.Piece, added));
        }
        yield return WaitUntil(() => session.GroundPreparationSnapshot(ship.ShipId).Value<int>("invalidations") >
                expanded.Value<int>("invalidations"), 15d, "正式撤销测试地板没有使扩展准备布局失效。");
        yield return WaitForPreparedGroundShip(ship);
        JObject finalReady = GroundPreparationLiveSnapshot(ship, ids, baselines, world);
        finalReady["previousCandidates"] = GroundCandidateRetirementReadings(expanded, finalReady);
        ShipPieceState wounded = FindGroundPreparationFloor(ship);
        double healthBefore = wounded.Health;
        bool accepted = session.ApplyPieceDamage(int.Parse(wounded.PieceId, CultureInfo.InvariantCulture),
            (float)Math.Min(1d, healthBefore * .01d), "隔离下降候选的最新健康值取样");
        var health = new JObject { ["guid"] = int.Parse(wounded.PieceId, CultureInfo.InvariantCulture),
            ["accepted"] = accepted, ["before"] = healthBefore, ["after"] = wounded.Health };
        completed(new JObject { ["cancellation"] = cancellation, ["ready"] = ready, ["moved"] = moved,
            ["expanded"] = expanded, ["finalReady"] = finalReady, ["addedId"] = addedId,
            ["addedPieceStillPresent"] = session.FindPiece(addedId, out _, out _), ["healthChange"] = health });
    }

    #endregion
}
