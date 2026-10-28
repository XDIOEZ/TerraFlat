using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class GameplayTestSpaceScenario
{
    #region 小型矿石小行星真实掉落与拾取
    private readonly HashSet<string> oreFixtureDropIds = new(StringComparer.Ordinal);
    private readonly List<(SpaceOreAsteroidState State, double Health, bool Depleted)> oreFixtureDamage = new();

    // 正式会话负责致死和真实冷载荷，自动拾取仍由当前玩家拾取器提交库存。
    private IEnumerator ObserveOreAsteroidDestructionAndPickup(Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(CurrentPlayer());
        ProtectPlayer();
        session.GetUniverse(default);
        SpaceOreAsteroidState asteroid = session.State.OreAsteroids.FirstOrDefault(value => value.IsAlive) ??
            throw new InvalidOperationException("隔离宇宙没有生成可击碎的小型矿石小行星。");
        oreFixtureDamage.Add((asteroid, asteroid.HitPoints, asteroid.Depleted));
        SpaceOreAsteroidComposition composition = SpaceOreAsteroidCatalog.LoadDefault().GetComposition(asteroid.CompositionId);
        SpaceSectorPosition position = session.GetOreAsteroidPosition(asteroid);
        BodyState center = session.GetBody(asteroid.CenterBodyId);
        yield return Teleport(new SpaceMotionState { Sector = position.Sector,
            PositionMeters = position.LocalMeters + new SpaceVector2(asteroid.RadiusMeters + 5d, 0d),
            VelocityMetersPerSecond = center.VelocityMetersPerSecond, RadiusMeters = .3d });
        float beforeOre = CountCurrentPickerInventoryItem(composition.ItemId);
        float beforeStone = CountCurrentPickerInventoryItem("Ore_Stone");
        int beforeDropCount = session.State.LooseItems.Count;
        double initialHealth = asteroid.HitPoints;
        double partial = session.ApplyResolvedOreAsteroidDamage(asteroid, initialHealth * .25d);
        double afterPartial = asteroid.HitPoints;
        int afterPartialDrops = session.State.LooseItems.Count;
        var oldIds = new HashSet<string>(session.State.LooseItems.Select(value => value.Id), StringComparer.Ordinal);
        double lethal = session.ApplyResolvedOreAsteroidDamage(asteroid, initialHealth * 2d);
        List<SpaceLooseItemState> drops = session.State.LooseItems.Where(value => !oldIds.Contains(value.Id)).ToList();
        var rows = new JArray();
        var inventoryBeforeDamage = new Dictionary<string, JArray>(StringComparer.Ordinal);
        foreach (SpaceLooseItemState drop in drops)
        {
            oreFixtureDropIds.Add(drop.Id);
            ItemData item = SpaceSession.Decode(drop.Snapshot);
            if (!inventoryBeforeDamage.ContainsKey(item.IDName))
                inventoryBeforeDamage.Add(item.IDName, CaptureCurrentPickerInventoryItemSlots(item.IDName));
            rows.Add(new JObject { ["id"] = drop.Id, ["itemId"] = item.IDName,
                ["guid"] = item.Guid, ["amount"] = item.Stack.Amount,
                ["pickupAllowed"] = item.Stack.CanBePickedUp, ["inSpace"] = drop.InSpace,
                ["earlyPickup"] = drop.CanBePickedUpAt(session.State.SimulationSeconds) });
        }
        int beforeRepeated = session.State.LooseItems.Count;
        double repeated = session.ApplyResolvedOreAsteroidDamage(asteroid, initialHealth);
        int afterRepeated = session.State.LooseItems.Count;
        SpaceSessionState restored = SaveDataMgr.DeserializeSpaceState(JsonConvert.SerializeObject(session.State));
        SpaceOreAsteroidState saved = restored.OreAsteroids.Single(value => value.AsteroidId == asteroid.AsteroidId);
        bool savedSector = restored.GeneratedOreAsteroidSectors.Contains(asteroid.Sector);
        int generatedCount = session.State.OreAsteroids.Count;
        session.GetUniverse(asteroid.Sector);
        int afterReentry = session.State.OreAsteroids.Count;
        if (drops.Count == 0) throw new InvalidOperationException("正式小行星致死没有提交任何真实冷载荷。");
        var pickupResults = new JArray();
        for (int index = 0; index < drops.Count; index++)
        {
            SpaceLooseItemState drop = drops[index];
            JObject dropRow = (JObject)rows[index];
            string itemId = dropRow.Value<string>("itemId");
            bool alreadyPicked = !session.State.LooseItems.Contains(drop);
            float inventoryBeforeApproach = CountCurrentPickerInventoryItem(itemId);
            double? distanceBeforeApproach = null, distanceAfterApproach = null;
            bool presentAtArrival = false;
            if (!alreadyPicked)
            {
                // 掉落各自漂移，逐堆接近其当前权威位置并保持正式拾取事务。
                distanceBeforeApproach = DistanceFromPlayerToSpaceDrop(drop);
                yield return Teleport(new SpaceMotionState { Sector = drop.Motion.Sector,
                    PositionMeters = drop.Motion.PositionMeters,
                    VelocityMetersPerSecond = drop.Motion.VelocityMetersPerSecond, RadiusMeters = .3d });
                presentAtArrival = session.State.LooseItems.Contains(drop);
                distanceAfterApproach = DistanceFromPlayerToSpaceDrop(drop);
                yield return WaitUntil(() => !session.State.LooseItems.Contains(drop), 20d,
                    "真实小行星掉矿没有被玩家自动拾取器完整放入库存。",
                    () => DescribeOreAsteroidPickupFailure(drop, itemId, distanceBeforeApproach, distanceAfterApproach,
                        inventoryBeforeApproach, inventoryBeforeDamage[itemId], pickupResults));
            }
            JArray beforeSlots = inventoryBeforeDamage[itemId];
            JArray afterSlots = CaptureCurrentPickerInventoryItemSlots(itemId);
            float beforeCount = SumCapturedPickerInventoryItemSlots(beforeSlots);
            float afterCount = SumCapturedPickerInventoryItemSlots(afterSlots);
            pickupResults.Add(new JObject
            {
                ["id"] = drop.Id, ["itemId"] = itemId, ["alreadyPicked"] = alreadyPicked,
                ["approached"] = !alreadyPicked, ["presentAtArrival"] = presentAtArrival,
                ["distanceBeforeApproach"] = distanceBeforeApproach, ["distanceAfterApproach"] = distanceAfterApproach,
                ["removedFromSpace"] = !session.State.LooseItems.Contains(drop),
                ["inventoryBeforeApproach"] = inventoryBeforeApproach,
                ["inventoryBeforeDamage"] = beforeCount, ["inventoryAfterPickup"] = afterCount,
                ["inventoryGainDuringApproach"] = afterCount - inventoryBeforeApproach,
                ["inventoryGain"] = afterCount - beforeCount,
                ["inventorySlotsBeforeDamage"] = beforeSlots.DeepClone(), ["inventorySlotsAfterPickup"] = afterSlots
            });
        }
        completed(new JObject
        {
            ["scope"] = "live-authoritative-asteroid-drops-and-player-pickup",
            ["asteroidId"] = asteroid.AsteroidId, ["compositionItem"] = composition.ItemId,
            ["oreAmount"] = asteroid.OreAmount, ["stoneAmount"] = asteroid.StoneAmount,
            ["initialHealth"] = initialHealth, ["partialApplied"] = partial, ["afterPartial"] = afterPartial,
            ["beforeDropCount"] = beforeDropCount, ["afterPartialDrops"] = afterPartialDrops,
            ["lethalApplied"] = lethal, ["health"] = asteroid.HitPoints, ["depleted"] = asteroid.Depleted,
            ["drops"] = rows, ["pickupResults"] = pickupResults, ["repeatedApplied"] = repeated,
            ["beforeRepeatedDrops"] = beforeRepeated, ["afterRepeatedDrops"] = afterRepeated,
            ["savedDepleted"] = saved.Depleted, ["savedHealth"] = saved.HitPoints, ["savedSector"] = savedSector,
            ["generatedCount"] = generatedCount, ["afterReentry"] = afterReentry,
            ["oreInventoryGain"] = CountCurrentPickerInventoryItem(composition.ItemId) - beforeOre,
            ["stoneInventoryGain"] = CountCurrentPickerInventoryItem("Ore_Stone") - beforeStone,
            ["allDropsRemoved"] = drops.All(value => !session.State.LooseItems.Contains(value)),
            ["limitations"] = "正式会话结算、真实冷载荷和玩家库存拾取；不覆盖武器动画及命中几何。"
        });
    }

    private double DistanceFromPlayerToSpaceDrop(SpaceLooseItemState drop)
    {
        SpaceSectorPosition player = session.GetPassengerPosition(session.GetPassenger(CurrentPlayer()));
        return SpaceSectorCoordinates.Difference(new SpaceSectorPosition(drop.Motion.Sector, drop.Motion.PositionMeters), player).Magnitude;
    }

    private JArray CaptureCurrentPickerInventoryItemSlots(string itemId)
    {
        Mod_ItemPicker picker = CurrentPlayer().itemMods.GetMod_ByID<Mod_ItemPicker>(ModText.Picker) ??
            throw new InvalidOperationException("当前测试玩家缺少正式物品拾取器。");
        var slots = new JArray();
        var visited = new HashSet<Inventory_Data>();
        for (int inventoryIndex = 0; inventoryIndex < picker.AddTargetInventories.Count; inventoryIndex++)
        {
            Inventory_Data inventory = picker.AddTargetInventories[inventoryIndex]?.GetDefaultTargetInventory()?.Data;
            if (inventory == null || !visited.Add(inventory)) continue;
            for (int slotIndex = 0; slotIndex < inventory.itemSlots.Count; slotIndex++)
            {
                ItemData item = inventory.itemSlots[slotIndex].itemData;
                if (item?.IDName != itemId) continue;
                slots.Add(new JObject { ["inventoryIndex"] = inventoryIndex, ["inventoryName"] = inventory.Name,
                    ["slotIndex"] = slotIndex, ["guid"] = item.Guid, ["amount"] = item.Stack.Amount });
            }
        }
        return slots;
    }

    private static float SumCapturedPickerInventoryItemSlots(JArray slots)
        => slots.Sum(value => value.Value<float>("amount"));

    private string DescribeOreAsteroidPickupFailure(SpaceLooseItemState drop, string itemId,
        double? distanceBeforeApproach, double? distanceAfterApproach, float inventoryBeforeApproach,
        JArray beforeSlots, JArray completedPickups)
    {
        Player player = CurrentPlayer();
        SpacePassengerState passenger = session.GetPassenger(player);
        Mod_ItemPicker picker = player.itemMods.GetMod_ByID<Mod_ItemPicker>(ModText.Picker);
        Vector2 point = session.ProjectPosition(new SpaceSectorPosition(drop.Motion.Sector, drop.Motion.PositionMeters));
        Bounds bounds = default;
        bool hasBounds = picker != null && picker.TryGetDroppedPickupBounds(out bounds);
        return new JObject
        {
            ["dropId"] = drop.Id, ["itemId"] = itemId, ["remaining"] = SpaceSession.Decode(drop.Snapshot).Stack.Amount,
            ["dropWorld"] = drop.WorldKey, ["dropSector"] = drop.Motion.Sector.ToString(),
            ["dropX"] = drop.Motion.PositionMeters.X, ["dropY"] = drop.Motion.PositionMeters.Y,
            ["dropVelocityX"] = drop.Motion.VelocityMetersPerSecond.X, ["dropVelocityY"] = drop.Motion.VelocityMetersPerSecond.Y,
            ["playerScene"] = player.gameObject.scene.name, ["activeScene"] = SceneManager.GetActiveScene().name,
            ["executionWorld"] = GameplayWorldRegistry.ExecutionWorldKey, ["spaceView"] = session.IsSpaceView,
            ["playerVelocityX"] = passenger.FreeMotion.VelocityMetersPerSecond.X,
            ["playerVelocityY"] = passenger.FreeMotion.VelocityMetersPerSecond.Y,
            ["distanceBeforeApproach"] = distanceBeforeApproach, ["distanceAfterApproach"] = distanceAfterApproach,
            ["distance"] = DistanceFromPlayerToSpaceDrop(drop), ["projectedX"] = point.x, ["projectedY"] = point.y,
            ["pickerFound"] = picker != null, ["pickerScene"] = picker != null ? picker.gameObject.scene.name : null,
            ["pickerCanPickUp"] = picker != null && picker.CanPickUp, ["pickerHasBounds"] = hasBounds,
            ["inPickerBounds"] = hasBounds && bounds.Contains(new Vector3(point.x, point.y, bounds.center.z)),
            ["pickerCanReach"] = picker != null && picker.CanReachDroppedPoint(point),
            ["pickupCooldownElapsed"] = drop.CanBePickedUpAt(session.State.SimulationSeconds),
            ["inventoryBeforeApproach"] = inventoryBeforeApproach,
            ["inventorySlotsBeforeDamage"] = beforeSlots.DeepClone(),
            ["inventorySlotsNow"] = picker != null ? CaptureCurrentPickerInventoryItemSlots(itemId) : new JArray(),
            ["completedPickups"] = completedPickups.DeepClone()
        }.ToString(Formatting.None);
    }

    private float CountCurrentPickerInventoryItem(string itemId)
    {
        Mod_ItemPicker picker = CurrentPlayer().itemMods.GetMod_ByID<Mod_ItemPicker>(ModText.Picker) ??
            throw new InvalidOperationException("当前测试玩家缺少正式物品拾取器。");
        float count = 0f;
        var visited = new HashSet<Inventory_Data>();
        foreach (IInventory provider in picker.AddTargetInventories)
        {
            Inventory_Data inventory = provider?.GetDefaultTargetInventory()?.Data;
            if (inventory == null || !visited.Add(inventory)) continue;
            foreach (ItemSlot slot in inventory.itemSlots)
                if (slot.itemData?.IDName == itemId) count += slot.itemData.Stack.Amount;
        }
        return count;
    }

    private void CleanupOreAsteroidFixtures()
    {
        if (session?.State != null)
            session.State.LooseItems.RemoveAll(value => oreFixtureDropIds.Contains(value.Id));
        oreFixtureDropIds.Clear();
        foreach (var fixture in oreFixtureDamage)
        { fixture.State.HitPoints = fixture.Health; fixture.State.Depleted = fixture.Depleted; }
        oreFixtureDamage.Clear();
    }
    #endregion

    #region 大型矿石小行星正式降落与地表开采
    private IEnumerator LandOwnedShipOnOreAsteroidAndMine(ShipState ship, string console, HashSet<int> ids,
        Action<JObject> completed)
    {
        // 目标星球的冷采样和实际生成共用正式配置，此租约在本专项返回前释放。
        using IDisposable naturalGeneration = GameplayTestShowroom.SuppressProfileOverrides();
        UniverseSimulation universe = session.GetUniverse(default);
        BodyState body = universe.State.Bodies.First(value => value.TemplateBodyId == "asteroid_iron");
        ShipFlightState flight = session.GetFlight(ship.ShipId) ?? throw new InvalidOperationException("测试船没有正式飞行状态。");
        string expectedWorld = body.SurfaceAddress.WorldKey;
        // 只预置本用例刚起飞的船到目标旁，正式降落、燃料结算和建筑移交全部保持真实流程。
        SetOwnedShipPose(ship, flight, body.Sector,
            body.PositionMeters + new SpaceVector2(body.RadiusMeters + 25d, 0d), body.VelocityMetersPerSecond);
        yield return Wait(.04d);
        if (!session.RequestLanding(console)) throw new InvalidOperationException("正式船控拒绝大型矿石小行星降落。");
        IsMigrationExpected = true;
        yield return WaitUntil(() => Ready() && session.GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.SurfaceDescending,
            120d, "大型矿石小行星没有进入正式地表下降。");
        yield return new WaitForEndOfFrame();
        JObject descending = ShipSnapshot(ship);
        descending["flightHud"] = FlightHudSnapshot();
        yield return WaitUntil(() => Ready() && session.GetShip(ship.ShipId) == null && GroundBuildingsRestored(ids, expectedWorld),
            180d, "大型矿石小行星下降没有完成普通建筑移交。", () => ShipSnapshot(ship).ToString(Formatting.None));
        IsMigrationExpected = false;
        yield return new WaitForEndOfFrame();
        JObject landed = GroundBuildingSnapshot(ids);
        landed["flightHud"] = FlightHudSnapshot();
        var generation = ChunkMgr.Instance.ActiveGenerationProfile;
        var generationSample = new JObject
        {
            ["showroomOverride"] = generation.NumericParameters.TryGetValue("testing.showroom", out double marker) && marker > 0d,
            ["ecologyMultiplier"] = generation.EcologyGlobalMultiplier,
            ["ironMineRule"] = generation.EcologyRules.Any(rule => rule.ItemId == "Mine_Iron")
        };
        var targets = new List<NaturalEntityTargetObservation>();
        NaturalEntityTargetObservation mine = default;
        JObject generatedMine = null;
        yield return LocateAndActivateGeneratedAsteroidIronMine(value => generatedMine = value);
        int generatedGuid = generatedMine.Value<int>("guid");
        bool found = false;
        yield return WaitUntil(() =>
        {
            NaturalEntityEcsService.QueryTargetObservations(CurrentPlayer(), 96f, targets);
            foreach (NaturalEntityTargetObservation candidate in targets)
                if (candidate.ItemId == "Mine_Iron" && candidate.Target.TargetGuid == generatedGuid &&
                    candidate.HasHealth && candidate.Health.x > 0f)
                { mine = candidate; found = true; return true; }
            return false;
        }, 30d, "大型矿石小行星已落地，但加载地表没有正式铁矿山实体。", () => new JObject
        {
            ["generation"] = generationSample, ["generatedMine"] = generatedMine,
            ["entityCount"] = NaturalEntityEcsService.Count, ["targetCount"] = targets.Count,
            ["targets"] = new JArray(targets.Select(value => new JObject { ["id"] = value.ItemId,
                ["guid"] = value.Target.TargetGuid, ["hasHealth"] = value.HasHealth, ["health"] = value.Health.x }))
        }.ToString());
        Vector2 minePoint = mine.AimBounds.center;
        Camera camera = Camera.main ?? throw new InvalidOperationException("采矿现场缺少玩家镜头。");
        Mod_PlayerTraits traits = CurrentPlayer().itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        if (traits == null || !traits.TryTeleportToScreenPosition(camera.WorldToScreenPoint(minePoint + Vector2.right)))
            throw new InvalidOperationException("正式GM入口不能将隔离玩家放到矿山旁。");
        float beforeOre = CountCurrentPickerInventoryItem("Ore_Iron");
        var drops = new List<DroppedItemObservation>();
        DroppedItemService.QueryNearbyLightweightDrops(minePoint, 8f, drops);
        var existingDropIds = new HashSet<int>(drops.Select(value => value.Id));
        CombatIdentity source = GameplayCombatBridge.Identity(CurrentPlayer());
        var context = new CombatDamageContext
        {
            Attack = new CombatAttackKey { Source = source, Sequence = 1 }, Credit = source,
            SourceIsPlayer = 1, ResourceToolKind = (int)ResourceToolKind.Pickaxe, ResourceToolTier = 3,
            ResourceToolEfficiency = 1f, Damage = new float4(0f, 0f, 0f, mine.Health.x * 10f + 1000f),
            Origin = (Vector2)CurrentPlayer().transform.position, HitPoint = minePoint,
            Clock = new CombatClock { Time = Time.timeAsDouble, DeltaTime = Time.fixedDeltaTime }
        };
        float applied = NaturalEntityEcsService.ApplyDamage(mine.Handle, context);
        yield return WaitUntil(() => !NaturalEntityEcsService.Contains(mine.Handle),
            30d, "正式采矿伤害没有移除天然矿山。", () => new JObject
            { ["guid"] = mine.Target.TargetGuid, ["healthBefore"] = mine.Health.x, ["applied"] = applied }.ToString());
        int minedDropId = 0;
        bool automaticOrePickup = false;
        yield return WaitUntil(() =>
        {
            if (CountCurrentPickerInventoryItem("Ore_Iron") > beforeOre)
            { automaticOrePickup = true; return true; }
            DroppedItemService.QueryNearbyLightweightDrops(minePoint, 8f, drops);
            foreach (DroppedItemObservation drop in drops)
                if (drop.ItemId == "Ore_Iron" && drop.Pickable && !existingDropIds.Contains(drop.Id))
                {
                    // 战利品会随机散落，移动到真实新掉落点后由正常拾取器入包。
                    if (!traits.TryTeleportToWorldPosition(drop.Position))
                        throw new InvalidOperationException("正式 GM 入口不能移动到新开采矿石旁。");
                    minedDropId = drop.Id;
                    return true;
                }
            return false;
        }, 30d, "天然矿山移除后没有产生可拾取的新铁矿石。", () => new JObject
        { ["applied"] = applied, ["dropCount"] = DroppedItemService.Count,
            ["nearby"] = new JArray(drops.Select(value => new JObject { ["id"] = value.Id,
                ["itemId"] = value.ItemId, ["pickable"] = value.Pickable, ["amount"] = value.Amount })) }.ToString());
        yield return WaitUntil(() => CountCurrentPickerInventoryItem("Ore_Iron") > beforeOre,
            30d, "移动到真实矿石落点后，正常拾取器仍没有将矿石放入背包。");
        completed(new JObject
        {
            ["bodyId"] = body.BodyId, ["planetId"] = body.PlanetId, ["expectedWorld"] = expectedWorld,
            ["world"] = SceneManager.GetActiveScene().name, ["descending"] = descending, ["landed"] = landed,
            ["generation"] = generationSample,
            ["generatedMine"] = generatedMine, ["mineGuid"] = mine.Target.TargetGuid,
            ["shipRemoved"] = session.GetShip(ship.ShipId) == null, ["flightRemoved"] = session.GetFlight(ship.ShipId) == null,
            ["mineFound"] = found, ["mineId"] = mine.ItemId, ["mineHealth"] = mine.Health.x,
            ["miningApplied"] = applied, ["mineRemoved"] = !NaturalEntityEcsService.Contains(mine.Handle),
            ["minedDropId"] = minedDropId,
            ["automaticOrePickup"] = automaticOrePickup,
            ["oreInventoryGain"] = CountCurrentPickerInventoryItem("Ore_Iron") - beforeOre,
            ["limitations"] = "正式测试船起飞、降落及普通建筑移交，正式矿山受击与掉矿入包；不覆盖手动飞行导航、镐子动画和真实联机。"
        });
    }
    private IEnumerator LocateAndActivateGeneratedAsteroidIronMine(Action<JObject> completed)
    {
        // 只查正式区块基线并通过 GM 移动加载窗口，不创建矿山或改动生成概率。
        ChunkMgr chunks = ChunkMgr.Instance;
        var profile = chunks.ActiveGenerationProfile;
        Vector2 origin = WorldLocalPresentation.ToLogical(CurrentPlayer().transform.position);
        int seed = DimensionManager.Instance.GetActiveGenerationSeed(SaveDataMgr.Instance.SaveData.Seed);
        var visited = new HashSet<FlatWorld.WorldModel.WorldAddress>();
        int placements = 0;
        for (int radius = 0; radius <= 3; radius++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
            var address = chunks.ResolveWorldAddress(origin + new Vector2(x * profile.Width, y * profile.Height));
            if (!visited.Add(address)) continue;
            leases.Add(chunks.AcquireChunkLease(address, ChunkLeaseKind.DataRetention));
            var request = chunks.RequestChunkDataAsync(address, seed, profile, cancellation.Token);
            yield return WaitUntil(() => request.IsCompleted, 30d, "小行星矿山所在区块没有完成正式生成。");
            ChunkRuntime runtime = request.GetAwaiter().GetResult();
            foreach (NaturalItemPlacement placement in runtime.Ecology.Placements)
            {
                placements++;
                if (placement.ItemId != "Mine_Iron") continue;
                Vector2 point = placement.SharedAnchor.HasValue
                    ? new Vector2(placement.SharedAnchor.Value.X, placement.SharedAnchor.Value.Y)
                    : new Vector2(address.ChunkOrigin.X + placement.LocalX, address.ChunkOrigin.Y + placement.LocalY);
                point = WorldLocalPresentation.ToLogical(point + new Vector2(.5f + placement.OffsetX, .5f + placement.OffsetY));
                if (WorldTopologyRuntime.Distance(point, origin) <= 12f) continue;
                var traits = CurrentPlayer().itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
                if (traits == null || !traits.TryTeleportToWorldPosition(WorldLocalPresentation.ProjectPosition(point + Vector2.right * 2f)))
                    throw new InvalidOperationException("正式 GM 入口无法加载天然铁矿山所在窗口。");
                completed(new JObject { ["guid"] = placement.Guid, ["scannedChunks"] = visited.Count,
                    ["placements"] = placements, ["x"] = point.x, ["y"] = point.y });
                yield break;
            }
        }
        throw new InvalidOperationException($"小行星正式基线未生成可开采铁矿山：区块{visited.Count}，自然点{placements}。");
    }
    #endregion
}
