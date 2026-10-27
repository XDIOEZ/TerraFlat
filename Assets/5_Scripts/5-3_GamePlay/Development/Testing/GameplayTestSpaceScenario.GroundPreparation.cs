using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class GameplayTestSpaceScenario
{
    #region 降落准备的正式流程采样

    private readonly Dictionary<int, Item> groundOrdinaryItemInstances = new();
    private readonly Dictionary<int, List<Inventory>> groundOrdinaryInventories = new();
    private readonly Dictionary<int, string> groundOrdinaryInventoryPayloads = new();
    private readonly Dictionary<int, float> groundOrdinaryHealth = new();
    private readonly List<JObject> groundOrdinaryItemStages = new();

    private void ResetGroundOrdinaryItemBaseline()
    {
        groundOrdinaryItemInstances.Clear();
        groundOrdinaryInventories.Clear();
        groundOrdinaryInventoryPayloads.Clear();
        groundOrdinaryHealth.Clear();
        groundOrdinaryItemStages.Clear();
    }

    private JObject CaptureGroundOrdinaryItemStage(ICollection<int> ids, string worldKey,
        string stage, bool captureBaseline = false)
    {
        if (captureBaseline)
        {
            ResetGroundOrdinaryItemBaseline();
            var entries = new Dictionary<int, ShipGroundBuildingStore.Entry>();
            foreach (ShipGroundBuildingStore.Entry entry in ShipGroundBuildingStore.Collect(worldKey))
                if (ids.Contains(entry.Guid) && entry.Item != null) entries[entry.Guid] = entry;
            foreach (var pair in entries)
            {
                Item item = pair.Value.Item;
                groundOrdinaryItemInstances.Add(pair.Key, item);
                List<Inventory> inventories = CaptureItemInventories(item);
                groundOrdinaryInventories.Add(pair.Key, inventories);
                groundOrdinaryInventoryPayloads.Add(pair.Key, InventoryPayloadSignature(inventories));
                Mod_DamageReceiver health = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (health != null) groundOrdinaryHealth.Add(pair.Key, health.Hp);
            }
            if (groundOrdinaryItemInstances.Count == 0)
                throw new InvalidOperationException("起降夹具没有可追踪的真实普通 Item。");
        }

        var rows = new JArray();
        int sameInstances = 0, sameInventories = 0, sameHealth = 0;
        foreach (var baseline in groundOrdinaryItemInstances)
        {
            // Item 已随起降迁入船域时仍按全局正式运行时账本查同一实例。
            Item item = null;
            bool found = ItemMgr.Instance != null && ItemMgr.Instance.WorldRunTimeItems.TryGetValue(baseline.Key, out item);
            bool runtimeRegistered = item != null && ItemMgr.Instance.IsRuntimeItemRegistered(item) &&
                ItemMgr.Instance.WorldRunTimeItems.TryGetValue(baseline.Key, out Item indexed) && ReferenceEquals(indexed, item);
            string itemWorld = item != null ? GameplayWorldRegistry.GetItemWorldKey(item) : null;
            bool sameInstance = ReferenceEquals(item, baseline.Value) && runtimeRegistered && itemWorld == worldKey;
            List<Inventory> inventories = item != null ? CaptureItemInventories(item) : new List<Inventory>();
            List<Inventory> expectedInventories = groundOrdinaryInventories[baseline.Key];
            bool sameInventoryObjects = inventories.Count == expectedInventories.Count;
            for (int i = 0; sameInventoryObjects && i < inventories.Count; i++)
                sameInventoryObjects &= ReferenceEquals(inventories[i], expectedInventories[i]);
            string payload = InventoryPayloadSignature(inventories);
            bool sameInventoryPayload = payload == groundOrdinaryInventoryPayloads[baseline.Key];
            Mod_DamageReceiver health = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            bool sameItemHealth = !groundOrdinaryHealth.TryGetValue(baseline.Key, out float oldHealth) ||
                health != null && Math.Abs(health.Hp - oldHealth) < .0001f;
            if (sameInstance) sameInstances++;
            if (sameInventoryObjects && sameInventoryPayload) sameInventories++;
            if (sameItemHealth) sameHealth++;
            rows.Add(new JObject
            {
                ["guid"] = baseline.Key, ["itemId"] = item?.itemData?.IDName,
                ["instanceId"] = item != null ? item.GetInstanceID() : 0,
                ["itemWorldKey"] = itemWorld, ["runtimeRegistered"] = runtimeRegistered,
                ["sameRuntimeInstance"] = sameInstance,
                ["inventoryCount"] = inventories.Count, ["sameInventoryInstances"] = sameInventoryObjects,
                ["sameInventoryPayload"] = sameInventoryPayload,
                ["health"] = health?.Hp, ["sameHealth"] = sameItemHealth
            });
        }
        var result = new JObject
        {
            ["stage"] = stage, ["worldKey"] = worldKey,
            ["expectedItemCount"] = groundOrdinaryItemInstances.Count,
            ["sameInstanceCount"] = sameInstances, ["sameInventoryCount"] = sameInventories,
            ["sameHealthCount"] = sameHealth, ["items"] = rows
        };
        groundOrdinaryItemStages.Add((JObject)result.DeepClone());
        return result;
    }

    private static List<Inventory> CaptureItemInventories(Item item)
    {
        var result = new List<Inventory>();
        if (item?.itemMods?.Mods == null) return result;
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is Mod_Inventory storage && storage.InventoryInstances != null)
                foreach (Inventory inventory in storage.InventoryInstances)
                    if (inventory != null) result.Add(inventory);
        return result;
    }

    private static string InventoryPayloadSignature(IReadOnlyList<Inventory> inventories)
    {
        var signature = new StringBuilder();
        foreach (Inventory inventory in inventories)
        {
            signature.Append(inventory.Data?.Name).Append('|');
            if (inventory.Data?.itemSlots == null) continue;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
            {
                ItemData data = slot?.itemData;
                signature.Append(data?.Guid ?? 0).Append(':').Append(data?.IDName).Append(':')
                    .Append((data?.Stack?.Amount ?? 0f).ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
            signature.Append('|');
        }
        return signature.ToString();
    }

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
        int groundMatches = 0;
        foreach (ShipGroundBuildingStore.Entry entry in ShipGroundBuildingStore.Collect(world))
            if (ids.Contains(entry.Guid)) groundMatches++;
        ShipFlightState flight = session.GetFlight(ship.ShipId);
        result["shipPresent"] = ReferenceEquals(session.GetShip(ship.ShipId), ship);
        result["phase"] = flight?.Phase.ToString(); result["height"] = flight?.HeightMeters;
        result["position"] = Vector(new SpaceVector2(ship.PositionX, ship.PositionY));
        result["groundMatches"] = groundMatches;
        result["ordinaryItems"] = CaptureGroundOrdinaryItemStage(ids,
            MachineWorld.GetShipScopeKey(ship.ShipId), "surface-descending");
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

    private static ShipPieceState FindGroundPreparationFloor(ShipState ship)
    {
        foreach (ShipPieceState piece in ship.Pieces)
            if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor && piece.Width == 1 && piece.Height == 1 && piece.Health > 2d)
                return piece;
        throw new InvalidOperationException("测试船没有可单独取样的健康一格地板。");
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
        ShipPieceState wounded = FindGroundPreparationFloor(ship);
        double healthBefore = wounded.Health;
        int woundedId = int.Parse(wounded.PieceId, CultureInfo.InvariantCulture);
        bool accepted = session.ApplyPieceDamage(woundedId,
            (float)Math.Min(1d, healthBefore * .01d), "隔离下降候选的最新健康值取样");
        if (accepted && groundOrdinaryHealth.ContainsKey(woundedId))
            groundOrdinaryHealth[woundedId] = (float)wounded.Health;
        var health = new JObject { ["guid"] = woundedId,
            ["accepted"] = accepted, ["before"] = healthBefore, ["after"] = wounded.Health };
        completed(new JObject { ["ready"] = ready, ["moved"] = moved,
            ["expanded"] = expanded, ["finalReady"] = finalReady, ["addedId"] = addedId,
            ["addedPieceStillPresent"] = session.FindPiece(addedId, out _, out _), ["healthChange"] = health });
    }

    #endregion
}
