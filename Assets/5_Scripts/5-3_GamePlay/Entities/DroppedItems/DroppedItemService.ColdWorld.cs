using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class DroppedItemService
{
    #region 明确世界身份的后台机器实物事务
    private readonly struct WorldDrop
    {
        public readonly string World;
        public readonly DroppedItemSaveRecord Record;
        public readonly Item Legacy;
        public WorldDrop(string world, DroppedItemSaveRecord record, Item legacy = null)
        { World = world; Record = record; Legacy = legacy; }
        public ItemData Data => Legacy != null ? Legacy.itemData : Record?.Data;
        public Vector2 Position => Legacy != null ? Legacy.itemData.transform.position : Record.Position;
        public bool Valid => Legacy != null ? IsLoosePickable(Legacy) : Record?.Data?.Stack?.Amount >= 1f &&
            Record.Data.Stack.CanBePickedUp && !Record.HasFlight && WorldRecords(World)?.Contains(Record) == true;
    }
    private static readonly List<WorldDrop> worldDrops = new();
    private static readonly HashSet<int> worldDropIdentities = new();
    private static readonly Dictionary<string, uint> coldTransportSteps = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<int>> coldTransported = new(StringComparer.Ordinal);
    private static readonly HashSet<ItemData> coldRebasedData = new();
    private static GameSaveData coldWorldSave;
    private static void BindColdWorldSave()
    {
        GameSaveData save = SaveDataMgr.Instance?.SaveData;
        if (ReferenceEquals(save, coldWorldSave)) return;
        coldWorldSave = save; coldRebasedData.Clear(); coldTransportSteps.Clear(); coldTransported.Clear();
    }
    private static List<DroppedItemSaveRecord> WorldRecords(string world)
        => SaveDataMgr.Instance?.SaveData?.DroppedItems?.Worlds.TryGetValue(world, out var records) == true ? records : null;

    public static void TickColdWorld(string world, float seconds)
    {
        List<DroppedItemSaveRecord> records = WorldRecords(world);
        if (records == null || records.Count == 0 || ownerWorld == world && runtime != null) return;
        bool hasMoving = false;
        foreach (DroppedItemSaveRecord record in records) hasMoving |= record != null && (record.HasFlight || record.HasWaterTransition || record.WaterKind != 0);
        if (!hasMoving) return;
        GameSaveData save = SaveDataMgr.Instance.SaveData;
        WorldTopologyDomain domain = save.PlanetData_Dict.TryGetValue(world, out PlanetData planet) &&
            WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds) ? bounds.ToDomain() : default;
        using var simulation = new LightweightDroppedItemSimulation();
        foreach (DroppedItemSaveRecord record in records)
        {
            if (record?.Data?.Stack == null || !(record.HasFlight || record.HasWaterTransition || record.WaterKind != 0)) continue;
            var body = new LightweightDroppedBody { Id = record.Data.Guid, Amount = record.Data.Stack.Amount,
                Position = record.Position, Scale = record.Scale, Rotation = record.Rotation, VisualHeight = record.VisualHeight,
                LiquidDepth = record.LiquidDepth, WaterKind = record.WaterKind };
            LightweightDroppedFlight? flight = record.HasFlight ? new LightweightDroppedFlight { Start = record.FlightStart,
                End = record.FlightEnd, Control = record.FlightControl, Duration = record.FlightDuration,
                Elapsed = record.FlightElapsed, ArcHeight = record.ArcHeight, RotationSpeed = record.RotationSpeed } : null;
            LightweightDroppedWaterTransition? water = record.HasWaterTransition ? new LightweightDroppedWaterTransition {
                StartDepth = record.WaterStart, TargetDepth = record.WaterTarget, Duration = record.WaterDuration,
                Elapsed = record.WaterElapsed, RecedeDuration = record.WaterKind == 2 ? WorldItemWaterRules.SubmergedRecedeDuration : 0f } : null;
            simulation.Create(body, flight, water);
        }
        simulation.Step(seconds, domain, new List<LightweightDroppedChange>());
        for (int index = records.Count - 1; index >= 0; index--)
        {
            DroppedItemSaveRecord record = records[index];
            if (record?.Data == null || !(record.HasFlight || record.HasWaterTransition || record.WaterKind != 0)) continue;
            bool wasFlying = record.HasFlight;
            if (!simulation.Contains(record.Data.Guid)) { records.RemoveAt(index); continue; }
            LightweightDroppedBody body = simulation.Get(record.Data.Guid);
            record.Position = body.Position; record.Rotation = body.Rotation; record.VisualHeight = body.VisualHeight;
            record.LiquidDepth = body.LiquidDepth; record.WaterKind = body.WaterKind;
            record.Data.transform.position = new Vector3(record.Position.x, record.Position.y);
            record.HasFlight = simulation.TryGetFlight(body.Id, out LightweightDroppedFlight flight);
            if (record.HasFlight) record.FlightElapsed = flight.Elapsed;
            record.HasWaterTransition = simulation.TryGetWater(body.Id, out LightweightDroppedWaterTransition water);
            if (record.HasWaterTransition) record.WaterElapsed = water.Elapsed;
            record.Data.Stack.CanBePickedUp = !record.HasFlight;
            if (record.WaterKind == 2 && !record.HasWaterTransition) { records.RemoveAt(index); continue; }
            if (record.WaterKind != 0 && FlatWorld.Spaceflight.SpaceSurfaceQuery.TryGetWaterCurrent(world, record.Position, out RuntimeWaterCurrentSample current))
            {
                float speed = WorldItemWaterRules.ResolveDriftSpeed(current.Kind, current.Flow, record.Data.Stack);
                Vector2 destination = PressureExplosionQueue.NormalizeInWorld(world, record.Position + current.Direction * (speed * Mathf.Min(seconds, 1f)));
                if (FlatWorld.Spaceflight.SpaceSurfaceQuery.TrySampleLiquid(world, destination, out _, out _)) record.Position = destination;
            }
            if (record.WaterKind != 0 || wasFlying && !record.HasFlight) RefreshColdWater(world, record);
            record.Data.transform.position = new Vector3(record.Position.x, record.Position.y);
        }
    }

    private static void CollectWorldDrops(string world)
    {
        BindColdWorldSave();
        worldDrops.Clear(); worldDropIdentities.Clear();
        if (ItemMgr.Instance != null)
            foreach (Item item in ItemMgr.Instance.WorldRunTimeItems.Values)
                if (item != null && item.gameObject.scene.name == world && IsLoosePickable(item) && worldDropIdentities.Add(item.itemData.Guid))
                    worldDrops.Add(new WorldDrop(world, null, item));
        // 活动轻量世界由原运行体持有，档案只是上次捕获，不能双读双扣。
        if (ownerWorld == world && runtime != null) return;
        List<DroppedItemSaveRecord> records = WorldRecords(world);
        if (records == null) return;
        foreach (DroppedItemSaveRecord record in records)
            if (record?.Data != null && worldDropIdentities.Add(record.Data.Guid))
            {
                if (coldRebasedData.Add(record.Data)) ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, record.Data);
                worldDrops.Add(new WorldDrop(world, record));
            }
    }

    public static void AbsorbWorldFuel(MachineEntity furnace, Inventory_Data inventory)
    {
        if (!GameNetwork.HasStateAuthority || furnace == null || inventory?.itemSlots == null) return;
        string world = furnace.ScopeKey;
        Vector2 center = (Vector2)furnace.Cell + Vector2.one * .5f;
        CollectWorldDrops(world);
        worldDrops.Sort((left, right) => PressureExplosionQueue.DeltaInWorld(world, center, left.Position).sqrMagnitude
            .CompareTo(PressureExplosionQueue.DeltaInWorld(world, center, right.Position).sqrMagnitude));
        foreach (WorldDrop drop in worldDrops)
        {
            if (!drop.Valid || drop.Data.Tags?.Contains(Tag.CombustionFuel) != true || MachineWorld.CellOf(drop.Position) != furnace.Cell ||
                !Mod_Fuel.TryResolveItemData(drop.Data, out FuelData fuel) || !MachineDefinition.Positive(fuel.Fuel.x)) continue;
            ItemData moved = ItemInstanceDataFactory.CloneRuntime(drop.Data);
            if (!inventory.TryAddItem(moved, false, out float available)) continue;
            int amount = Mathf.FloorToInt(Mathf.Min(moved.Stack.Amount, available) + .0001f);
            if (amount <= 0) continue;
            if (amount < drop.Data.Stack.Amount) { int id; do id = Guid.NewGuid().GetHashCode(); while (id == 0 || id == moved.Guid); moved.Guid = id; }
            moved.Stack.Amount = amount;
            // 库存只扣实际已接纳的数量，满槽或扩展回调拒绝不会吞掉原实物。
            if (inventory.TryAddItem(moved, true, out float added) || added > 0f)
            {
                drop.Data.Stack.Amount -= Mathf.Min(amount, added);
                PublishWorldDrop(drop);
            }
        }
        worldDrops.Clear();
    }

    public static void TransportWorldDrops(MachineEntity belt, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || belt?.Definition.Transport == null || !MachineDefinition.Positive(seconds)) return;
        BindColdWorldSave();
        string world = belt.ScopeKey;
        if (!coldTransported.TryGetValue(world, out HashSet<int> moved)) coldTransported.Add(world, moved = new());
        if (!coldTransportSteps.TryGetValue(world, out uint previous) || previous != MachineWorld.TransportStep)
        { moved.Clear(); coldTransportSteps[world] = MachineWorld.TransportStep; }
        CollectWorldDrops(world);
        foreach (WorldDrop drop in worldDrops)
        {
            if (!drop.Valid || drop.Record?.WaterKind > 0 || MachineWorld.GetTransportAt(drop.Position) != belt || !moved.Add(drop.Data.Guid)) continue;
            IItemTransferPort output = new ColdDropOutput(drop);
            MachineWorld.TryReceiveConveyorDrop(output, drop.Position, seconds, out bool terminal);
            if (terminal || !drop.Valid) continue;
            Vector2 position = MachineWorld.TransportGroundItem(drop.Position, seconds);
            drop.Data.transform.position = new Vector3(position.x, position.y);
            if (drop.Legacy != null)
            {
                drop.Legacy.transform.position = new Vector3(position.x, position.y, drop.Legacy.transform.position.z);
                Rigidbody2D body = drop.Legacy.GetComponent<Rigidbody2D>();
                if (body != null) { body.position = position; body.velocity = Vector2.zero; }
                ItemMgr.Instance.NotifyRuntimeItemMoved(drop.Legacy);
                ItemNetworkStateSerialization.NotifyRuntimeStateChanged(drop.Legacy);
            }
            else { drop.Record.Position = position; RefreshColdWater(world, drop.Record); }
        }
        worldDrops.Clear();
    }

    private static void PublishWorldDrop(WorldDrop drop)
    {
        if (drop.Legacy != null)
        {
            if (drop.Data.Stack.Amount <= 0f) drop.Legacy.DestroySelf();
            else { drop.Legacy.OnUIRefresh?.Invoke(); ItemNetworkStateSerialization.NotifyRuntimeStateChanged(drop.Legacy); }
        }
        else if (drop.Data.Stack.Amount <= 0f) WorldRecords(drop.World)?.Remove(drop.Record);
    }

    private static void RefreshColdWater(string world, DroppedItemSaveRecord record)
    {
        if (record.HasFlight || !WorldAddress.FromWorldKey(world).IsSurface) return;
        if (!FlatWorld.Spaceflight.SpaceSurfaceQuery.TrySampleLiquid(world, record.Position, out LiquidDefinition liquid, out _) ||
            FlatWorld.Spaceflight.SpaceSurfaceQuery.GetLandingSurface(world, record.Position).SupportTileId != 0)
        { record.WaterKind = 0; record.LiquidDepth = 0f; record.HasWaterTransition = false; return; }
        bool entering = record.WaterKind == 0;
        GameRes resources = GameRes.ExistingInstance;
        if (entering && liquid.WorldWater?.WaterContact == true && resources.TryGetItemDefinition(record.Data.IDName, out RuntimeItemDefinition definition) &&
            !string.IsNullOrWhiteSpace(definition.WaterEntryTransformItemId))
        {
            ItemData replacement = resources.CreateItemData(definition.WaterEntryTransformItemId);
            if (replacement?.Stack == null || !resources.TryGetItemDefinition(replacement.IDName, out RuntimeItemDefinition targetDefinition) || targetDefinition.IsActor)
                throw new InvalidOperationException($"无效的入水掉落物转换：{record.Data.IDName}");
            replacement.Guid = record.Data.Guid; replacement.Stack.Amount = record.Data.Stack.Amount;
            replacement.transform.position = record.Data.transform.position;
            replacement.inHand = false; replacement.Stack.CanBePickedUp = true;
            record.Data = replacement; record.Rotation = 0f;
        }
        float ratio = WorldItemWaterRules.ResolveWeightVolumeRatio(record.Data.Stack);
        byte kind = (byte)(WorldItemWaterRules.ShouldSink(ratio, WorldItemWaterRules.ResolveSinkRatioThreshold(liquid.BuoyancyThresholdMultiplier)) ? 2 : 1);
        if (!entering && kind == record.WaterKind) return;
        float target = kind == 1 ? WorldItemWaterRules.ResolveFloatingDepth(ratio) : 1f;
        float start = entering ? kind == 1 ? Mathf.Max(WorldItemWaterRules.FloatingEntryDepth, target) : 0f : record.LiquidDepth;
        record.WaterKind = kind; record.LiquidDepth = start; record.HasWaterTransition = true;
        record.WaterStart = start; record.WaterTarget = target; record.WaterElapsed = 0f;
        record.WaterDuration = kind == 1 ? WorldItemWaterRules.FloatingRiseDuration : WorldItemWaterRules.ResolveSinkDuration(ratio);
    }

    private sealed class ColdDropOutput : ContainerPortBase, IItemTransferPort
    {
        private readonly WorldDrop drop;
        public ColdDropOutput(WorldDrop drop) : base("cold-drop:" + drop.World + ":" + drop.Data.Guid, "dropped-output",
            new ContainerPortConfiguration { Id = "dropped-output", Type = "core:dropped_item", Direction = ContainerPortDirection.Output,
                Access = ContainerAccessKind.Machine, Reach = 2f }, () => drop.Valid, position: drop.Position,
            sceneHandle: SceneManager.GetSceneByName(drop.World).handle) { this.drop = drop; }
        public override object StorageIdentity => drop.Data;
        public override long StateVersion => drop.Data.Stack.Amount.GetHashCode();
        public override bool CanAccess(ContainerTransferContext context, out ContainerTransferFailure failure)
        {
            failure = ContainerTransferFailure.None;
            if (!drop.Valid) { failure = ContainerTransferFailure.StaleReference; return false; }
            if (context.Access != ContainerAccessKind.Machine || context.SceneHandle != SceneManager.GetSceneByName(drop.World).handle ||
                PressureExplosionQueue.DeltaInWorld(drop.World, drop.Position, context.Position).sqrMagnitude > 4f)
            { failure = ContainerTransferFailure.OutOfRange; return false; }
            return true;
        }
        public ItemData PeekItem(string itemId = null, string tag = null) => drop.Valid &&
            (itemId == null || itemId == drop.Data.IDName) && (tag == null || drop.Data.Tags?.Contains(tag) == true) ? drop.Data : null;
        public int GetReceivableItems(ItemData item, int requested) => 0;
        public bool InsertItems(ItemData item, int amount) => false;
        public bool ExtractItems(ItemData expected, int amount, out ItemData extracted)
        {
            extracted = null;
            if (!drop.Valid || expected.Guid != drop.Data.Guid || amount <= 0 || amount > drop.Data.Stack.Amount) return false;
            extracted = ItemInstanceDataFactory.CloneRuntime(drop.Data); extracted.Stack.Amount = amount;
            if (amount < drop.Data.Stack.Amount) { int id; do id = Guid.NewGuid().GetHashCode(); while (id == 0 || id == extracted.Guid); extracted.Guid = id; }
            drop.Data.Stack.Amount -= amount; return true;
        }
        public override object CaptureState() => drop.Data.Stack.Amount;
        public override void RestoreState(object snapshot) => drop.Data.Stack.Amount = (float)snapshot;
        public override void PublishState() => PublishWorldDrop(drop);
    }
    #endregion
}
