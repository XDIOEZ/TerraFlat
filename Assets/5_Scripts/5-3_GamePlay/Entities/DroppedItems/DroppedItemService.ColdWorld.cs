using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using FlatWorld.Networking;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class DroppedItemService
{
    #region 明确世界身份的后台机器实物事务
    internal readonly struct WorldDrop
    {
        public readonly string World;
        public readonly DroppedItemSaveRecord Record;
        public readonly Item Legacy;
        private readonly List<DroppedItemSaveRecord> sourceRecords;
        private readonly int recordIndex;
        public WorldDrop(string world, DroppedItemSaveRecord record, Item legacy = null,
            List<DroppedItemSaveRecord> records = null, int index = -1)
        { World = world; Record = record; Legacy = legacy; sourceRecords = records; recordIndex = index; }
        public ItemData Data => Legacy != null ? Legacy.itemData : Record?.Data;
        public Vector2 Position => Legacy != null ? Legacy.itemData.transform.position : Record.Position;
        public bool Valid => Legacy != null ? GameplayWorldRegistry.GetItemWorldKey(Legacy) == World && IsLoosePickable(Legacy) : Record?.Data?.Stack?.Amount >= 1f &&
            Record.Data.Stack.CanBePickedUp && !Record.HasFlight && IsCurrentRecord(WorldRecords(World));

        // 原下标仍匹配时直接确认身份，扩展回调移动列表后才回退查找。
        internal bool IsCurrentRecord(List<DroppedItemSaveRecord> records)
            => records != null && ReferenceEquals(records, sourceRecords) &&
               ((uint)recordIndex < (uint)records.Count && ReferenceEquals(records[recordIndex], Record) || records.Contains(Record));
    }

    internal readonly struct ColdFuelCandidate
    {
        internal readonly WorldDrop Drop;
        internal readonly float DistanceSquared;
        internal ColdFuelCandidate(WorldDrop drop, float distanceSquared)
        { Drop = drop; DistanceSquared = distanceSquared; }
    }

    private static readonly List<ColdFuelCandidate> coldFuelCandidates = new();
    private static readonly Comparison<ColdFuelCandidate> compareColdFuelDistance =
        (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared);
    private static readonly ProfilerMarker coldFuelCollectMarker = new("DroppedItemService.ColdFuel.Collect");
    private static readonly ProfilerMarker coldFuelSortMarker = new("DroppedItemService.ColdFuel.Sort");
    private static readonly ProfilerMarker coldFuelTransferMarker = new("DroppedItemService.ColdFuel.Transfer");
    private static readonly List<WorldDrop> worldDrops = new();
    private static readonly HashSet<int> worldDropIdentities = new();
    private static readonly Dictionary<string, uint> coldTransportSteps = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<int>> coldTransported = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (int Frame, HashSet<int> Items)> coldEnvironmentMotion = new(StringComparer.Ordinal);
    private static readonly HashSet<ItemData> coldRebasedData = new();
    private static GameSaveData coldWorldSave;
    private static void BindColdWorldSave()
    {
        GameSaveData save = SaveDataMgr.Instance?.SaveData;
        if (ReferenceEquals(save, coldWorldSave)) return;
        coldWorldSave = save; coldRebasedData.Clear(); coldTransportSteps.Clear(); coldTransported.Clear();
        coldEnvironmentMotion.Clear();
    }
    private static List<DroppedItemSaveRecord> WorldRecords(string world)
        => SaveDataMgr.Instance?.SaveData?.DroppedItems?.Worlds.TryGetValue(world, out var records) == true ? records : null;

    private static readonly LightweightDroppedItemSimulation coldMotion = new();
    private static readonly List<LightweightDroppedChange> coldMotionChanges = new();
    private static readonly HashSet<int> coldLanded = new();
    private static readonly ProfilerMarker coldWorldMotionMarker = new("DroppedItemService.ColdWorld.Motion");
    private static readonly ProfilerMarker coldWorldWaterMarker = new("DroppedItemService.ColdWorld.Water");

    public static void TickColdWorld(string world, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || !MachineDefinition.Positive(seconds)) return;
        BindColdWorldSave();
        List<DroppedItemSaveRecord> records = WorldRecords(world);
        if (records == null || records.Count == 0 || HasLiveWorld(world)) return;
        bool hasMoving = LooseItemMotion.HasVelocitySources;
        foreach (DroppedItemSaveRecord record in records) hasMoving |= record != null && (record.HasFlight || record.HasWaterTransition || record.WaterKind != 0);
        if (!hasMoving) return;
        GameSaveData save = SaveDataMgr.Instance.SaveData;
        WorldTopologyDomain domain = save.PlanetData_Dict.TryGetValue(world, out PlanetData planet) &&
            WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds) ? bounds.ToDomain() : default;
        using (coldWorldMotionMarker.Auto()) StepColdDropMotion(records, seconds, domain);
        if (!coldEnvironmentMotion.TryGetValue(world, out var ownership)) ownership = (Time.frameCount, new HashSet<int>());
        ownership.Items.Clear();
        coldEnvironmentMotion[world] = (Time.frameCount, ownership.Items);
        using var waterSample = coldWorldWaterMarker.Auto();
        for (int index = records.Count - 1; index >= 0; index--)
        {
            DroppedItemSaveRecord record = records[index];
            if (record?.Data?.Stack == null || record.HasFlight || !(record.HasWaterTransition || record.WaterKind != 0 ||
                coldLanded.Contains(record.Data.Guid) || LooseItemMotion.HasVelocitySources)) continue;
            if (record.WaterKind != 0)
            {
                Vector2 velocity = Vector2.zero;
                if (record.WaterKind != LightweightDroppedBody.BottomWaterKind)
                {
                    if (FlatWorld.Spaceflight.SpaceSurfaceQuery.TryGetWaterCurrent(world, record.Position, out RuntimeWaterCurrentSample current))
                        velocity = current.Direction * WorldItemWaterRules.ResolveDriftSpeed(current.Kind, current.Flow, record.Data.Stack);
                    record.Position = LooseItemMotion.AdvanceWater(world, record.Data, record.Position, velocity,
                        seconds, record.LiquidDepth < .995f);
                    ownership.Items.Add(record.Data.Guid);
                }
            }
            else if (LooseItemMotion.HasVelocitySources && record.Data.Stack.CanBePickedUp &&
                     !LooseItemMotion.IsDrivenByConveyor(record.Position))
            {
                record.Position = LooseItemMotion.AdvanceExternal(world, record.Data, record.Position, seconds, Vector2.zero);
                ownership.Items.Add(record.Data.Guid);
            }
            if (record.WaterKind != 0 || coldLanded.Contains(record.Data.Guid) || ownership.Items.Contains(record.Data.Guid))
                RefreshColdWater(world, record);
            record.Data.transform.position = new Vector3(record.Position.x, record.Position.y);
        }
    }

    // 每次从真实档案重填并写回，复用容量不形成第二份长期权威状态。
    internal static void StepColdDropMotion(List<DroppedItemSaveRecord> records, float seconds, WorldTopologyDomain domain)
    {
        coldLanded.Clear();
        coldMotion.Clear();
        try
        {
            foreach (DroppedItemSaveRecord record in records)
            {
                // 稳定水态直接保留档案，只有飞行和水线过渡需要重填短期运动工作区。
                if (record?.Data?.Stack == null || !(record.HasFlight || record.HasWaterTransition)) continue;
                LightweightDroppedBody body = record.RestoreBody(record.Data, domain);
                LightweightDroppedFlight? flight = record.HasFlight ? new LightweightDroppedFlight { Start = record.FlightStart,
                    End = record.FlightEnd, Control = record.FlightControl, Duration = record.FlightDuration,
                    Elapsed = record.FlightElapsed, ArcHeight = record.ArcHeight, RotationSpeed = record.RotationSpeed } : null;
                LightweightDroppedWaterTransition? water = record.HasWaterTransition ? new LightweightDroppedWaterTransition {
                    StartDepth = record.WaterStart, TargetDepth = record.WaterTarget, Duration = record.WaterDuration,
                    Elapsed = record.WaterElapsed, RecedeDuration = record.WaterKind == 2 ? WorldItemWaterRules.SubmergedRecedeDuration : 0f } : null;
                coldMotion.Create(body, flight, water);
            }
            coldMotion.Step(seconds, domain, coldMotionChanges);
            for (int index = records.Count - 1; index >= 0; index--)
            {
                DroppedItemSaveRecord record = records[index];
                if (record?.Data == null || !(record.HasFlight || record.HasWaterTransition)) continue;
                bool wasFlying = record.HasFlight;
                if (!coldMotion.Contains(record.Data.Guid)) { records.RemoveAt(index); continue; }
                LightweightDroppedBody body = coldMotion.Get(record.Data.Guid);
                record.Position = body.Position; record.Rotation = body.Rotation; record.VisualHeight = body.VisualHeight;
                record.LiquidDepth = body.LiquidDepth; record.WaterKind = body.WaterKind;
                record.Data.transform.position = new Vector3(record.Position.x, record.Position.y);
                record.HasFlight = coldMotion.TryGetFlight(body.Id, out LightweightDroppedFlight flight);
                if (record.HasFlight) record.FlightElapsed = flight.Elapsed;
                record.HasWaterTransition = coldMotion.TryGetWater(body.Id, out LightweightDroppedWaterTransition water);
                if (record.HasWaterTransition) record.WaterElapsed = water.Elapsed;
                record.Data.Stack.CanBePickedUp = !record.HasFlight;
                if (wasFlying && !record.HasFlight) coldLanded.Add(record.Data.Guid);
            }
        }
        finally { coldMotion.Clear(); coldMotionChanges.Clear(); }
    }

    private static void CollectWorldDrops(string world)
    {
        BindColdWorldSave();
        worldDrops.Clear(); worldDropIdentities.Clear();
        if (ItemMgr.Instance != null)
            foreach (Item item in ItemMgr.Instance.WorldRunTimeItems.Values)
                if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) == world && IsLoosePickable(item) && worldDropIdentities.Add(item.itemData.Guid))
                    worldDrops.Add(new WorldDrop(world, null, item));
        // 活动轻量世界由原运行体持有，档案只是上次捕获，不能双读双扣。
        if (HasLiveWorld(world)) return;
        List<DroppedItemSaveRecord> records = WorldRecords(world);
        if (records == null) return;
        for (int index = 0; index < records.Count; index++)
        {
            DroppedItemSaveRecord record = records[index];
            if (record?.Data != null && worldDropIdentities.Add(record.Data.Guid))
            {
                if (coldRebasedData.Add(record.Data)) ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, record.Data);
                worldDrops.Add(new WorldDrop(world, record, records: records, index: index));
            }
        }
    }

    public static void AbsorbWorldFuel(MachineEntity furnace, Inventory_Data inventory)
    {
        if (!GameNetwork.HasStateAuthority || furnace == null || inventory?.itemSlots == null) return;
        string world = furnace.ScopeKey;
        WorldTopologyDomain topology = ColdFuelTopology(world);
        try
        {
            using (coldFuelCollectMarker.Auto()) CollectWorldFuelCandidates(world, furnace.Cell, topology);
            if (coldFuelCandidates.Count == 0) return;
            using (coldFuelSortMarker.Auto()) SortColdFuelCandidates(coldFuelCandidates);
            using (coldFuelTransferMarker.Auto())
            foreach (ColdFuelCandidate candidate in coldFuelCandidates)
            {
                WorldDrop drop = candidate.Drop;
                if (!drop.Valid || ColdFuelCell(drop.Position, topology) != furnace.Cell ||
                    drop.Data.Tags?.Contains(Tag.CombustionFuel) != true ||
                    !Mod_Fuel.TryResolveItemData(drop.Data, out FuelData fuel) || !MachineDefinition.Positive(fuel.Fuel.x)) continue;
                if (TransferColdFuel(drop.Data, inventory) > 0f) PublishWorldDrop(drop);
            }
        }
        finally { coldFuelCandidates.Clear(); }
    }

    // 同格筛选在补配置和排序之前完成，远处掉落物不会进入炉子的候选工作区。
    private static void CollectWorldFuelCandidates(string world, Vector2Int cell, WorldTopologyDomain topology)
    {
        BindColdWorldSave();
        coldFuelCandidates.Clear(); worldDropIdentities.Clear();
        float2 center = new(cell.x + .5f, cell.y + .5f);
        if (ItemMgr.Instance != null)
            foreach (Item item in ItemMgr.Instance.WorldRunTimeItems.Values)
            {
                if (item == null || GameplayWorldRegistry.GetItemWorldKey(item) != world || !IsLoosePickable(item) ||
                    !worldDropIdentities.Add(item.itemData.Guid)) continue;
                Vector2 position = item.itemData.transform.position;
                if (ColdFuelCell(position, topology) == cell)
                    coldFuelCandidates.Add(new ColdFuelCandidate(new WorldDrop(world, null, item),
                        topology.SqrDistance(center, new float2(position.x, position.y))));
            }
        if (HasLiveWorld(world)) return;
        int firstCold = coldFuelCandidates.Count;
        CollectColdFuelCandidates(WorldRecords(world), world, cell, topology, worldDropIdentities, coldFuelCandidates);
        for (int index = firstCold; index < coldFuelCandidates.Count; index++)
        {
            ItemData data = coldFuelCandidates[index].Drop.Data;
            if (coldRebasedData.Add(data)) ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data);
        }
    }

    internal static void CollectColdFuelCandidates(List<DroppedItemSaveRecord> records, string world,
        Vector2Int cell, WorldTopologyDomain topology, HashSet<int> identities, List<ColdFuelCandidate> result)
    {
        if (records == null) return;
        float2 center = new(cell.x + .5f, cell.y + .5f);
        for (int index = 0; index < records.Count; index++)
        {
            DroppedItemSaveRecord record = records[index];
            if (record?.Data == null || !identities.Add(record.Data.Guid) ||
                ColdFuelCell(record.Position, topology) != cell || record.HasFlight ||
                record.Data.Stack == null || record.Data.Stack.Amount < 1f || !record.Data.Stack.CanBePickedUp) continue;
            result.Add(new ColdFuelCandidate(new WorldDrop(world, record, records: records, index: index),
                topology.SqrDistance(center, new float2(record.Position.x, record.Position.y))));
        }
    }

    internal static void SortColdFuelCandidates(List<ColdFuelCandidate> candidates) => candidates.Sort(compareColdFuelDistance);

    internal static Vector2Int ColdFuelCell(Vector2 position, WorldTopologyDomain topology)
    {
        int2 cell = topology.Normalize(new int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        return new Vector2Int(cell.x, cell.y);
    }

    private static WorldTopologyDomain ColdFuelTopology(string world)
    {
        var planets = SaveDataMgr.Instance?.SaveData?.PlanetData_Dict;
        if (planets == null || string.IsNullOrWhiteSpace(world) || world.StartsWith("ship:", StringComparison.Ordinal)) return default;
        if (!planets.TryGetValue(world, out PlanetData planet))
            planets.TryGetValue(WorldAddress.FromWorldKey(world).RootWorldKey, out planet);
        return WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds) ? bounds.ToDomain() : default;
    }

    internal static float TransferColdFuel(ItemData source, Inventory_Data inventory)
    {
        ItemData moved = ItemInstanceDataFactory.CloneRuntime(source);
        if (!inventory.TryAddItem(moved, false, out float available)) return 0f;
        int amount = Mathf.FloorToInt(Mathf.Min(moved.Stack.Amount, available) + .0001f);
        if (amount <= 0) return 0f;
        if (amount < source.Stack.Amount) { int id; do id = Guid.NewGuid().GetHashCode(); while (id == 0 || id == moved.Guid); moved.Guid = id; }
        moved.Stack.Amount = amount;
        // 库存只扣实际已接纳的数量，满槽或扩展回调拒绝不会吞掉原实物。
        if (!inventory.TryAddItem(moved, true, out float added) && added <= 0f) return 0f;
        float transferred = Mathf.Min(amount, added);
        source.Stack.Amount -= transferred;
        if (transferred > 0f) ItemDurabilityRules.AdvanceAfterRemoval(source);
        return transferred;
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
            if (!drop.Valid || drop.Record?.WaterKind > 0 || drop.Legacy?.GetComponent<WorldItemWaterRuntime>()?.IsActive == true ||
                MachineWorld.GetTransportAt(drop.Position) != belt || !moved.Add(drop.Data.Guid)) continue;
            // 水中物品本帧被外力推到带上后，不能再重复消费这帧的推动时间。
            if (drop.Record != null && coldEnvironmentMotion.TryGetValue(world, out var ownership) &&
                ownership.Frame == Time.frameCount && ownership.Items.Contains(drop.Data.Guid)) continue;
            IItemTransferPort output = new ColdDropOutput(drop);
            MachineWorld.TryReceiveConveyorDrop(output, drop.Position, seconds, out bool terminal);
            if (!drop.Valid) continue;
            Vector2 position = terminal
                ? LooseItemMotion.AdvanceExternal(world, drop.Data, drop.Position, seconds)
                : MachineWorld.TransportGroundItem(drop.Position, seconds, drop.Data);
            if (drop.Legacy != null)
                LooseItemMotion.CommitItemPosition(drop.Legacy, position);
            else
            {
                drop.Record.Position = position;
                drop.Data.transform.position = new Vector3(position.x, position.y);
                RefreshColdWater(world, drop.Record);
            }
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
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !FlatWorld.Spaceflight.SpaceSurfaceQuery.TryGetLandingSurface(world, record.Position, out var surface)) return;
        if (surface.LiquidDepth <= 0f || surface.SupportTileId != 0 ||
            !resources.TryGetLiquidDefinition(surface.LiquidId, out LiquidDefinition liquid) || liquid.WorldWater == null)
        { record.WaterKind = 0; record.LiquidDepth = 0f; record.HasWaterTransition = false; return; }
        bool entering = record.WaterKind == 0;
        if (record.WaterKind == LightweightDroppedBody.BottomWaterKind) return;
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
        public override long StateVersion => drop.Data.Stack.Amount.GetHashCode() ^ drop.Data.Durability.GetHashCode();
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
            drop.Data.Stack.Amount -= amount;
            ItemDurabilityRules.AdvanceAfterRemoval(drop.Data);
            return true;
        }
        public override object CaptureState() => (drop.Data.Stack.Amount, drop.Data.Durability);
        public override void RestoreState(object snapshot)
        {
            var state = ((float Amount, float Durability))snapshot;
            drop.Data.Durability = state.Durability;
            drop.Data.Stack.Amount = state.Amount;
        }
        public override void PublishState() => PublishWorldDrop(drop);
    }
    #endregion
}
