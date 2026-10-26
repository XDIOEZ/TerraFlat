using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using FlatWorld.Spaceflight;
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
        foreach (SpaceLooseItemState drop in drops)
        {
            oreFixtureDropIds.Add(drop.Id);
            ItemData item = SpaceSession.Decode(drop.Snapshot);
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
        SpaceLooseItemState oreDrop = drops.First(value => SpaceSession.Decode(value.Snapshot).IDName == composition.ItemId);
        yield return Teleport(new SpaceMotionState { Sector = oreDrop.Motion.Sector,
            PositionMeters = oreDrop.Motion.PositionMeters,
            VelocityMetersPerSecond = oreDrop.Motion.VelocityMetersPerSecond, RadiusMeters = .3d });
        yield return WaitUntil(() => drops.All(value => !session.State.LooseItems.Contains(value)), 20d,
            "真实小行星掉矿没有被玩家自动拾取器完整放入库存。");
        completed(new JObject
        {
            ["scope"] = "live-authoritative-asteroid-drops-and-player-pickup",
            ["asteroidId"] = asteroid.AsteroidId, ["compositionItem"] = composition.ItemId,
            ["oreAmount"] = asteroid.OreAmount, ["stoneAmount"] = asteroid.StoneAmount,
            ["initialHealth"] = initialHealth, ["partialApplied"] = partial, ["afterPartial"] = afterPartial,
            ["beforeDropCount"] = beforeDropCount, ["afterPartialDrops"] = afterPartialDrops,
            ["lethalApplied"] = lethal, ["health"] = asteroid.HitPoints, ["depleted"] = asteroid.Depleted,
            ["drops"] = rows, ["repeatedApplied"] = repeated,
            ["beforeRepeatedDrops"] = beforeRepeated, ["afterRepeatedDrops"] = afterRepeated,
            ["savedDepleted"] = saved.Depleted, ["savedHealth"] = saved.HitPoints, ["savedSector"] = savedSector,
            ["generatedCount"] = generatedCount, ["afterReentry"] = afterReentry,
            ["oreInventoryGain"] = CountCurrentPickerInventoryItem(composition.ItemId) - beforeOre,
            ["stoneInventoryGain"] = CountCurrentPickerInventoryItem("Ore_Stone") - beforeStone,
            ["allDropsRemoved"] = drops.All(value => !session.State.LooseItems.Contains(value)),
            ["limitations"] = "正式会话结算、真实冷载荷和玩家库存拾取；不覆盖武器动画及命中几何。"
        });
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
        JObject descending = ShipSnapshot(ship);
        yield return WaitUntil(() => Ready() && session.GetShip(ship.ShipId) == null && GroundBuildingsRestored(ids, expectedWorld),
            180d, "大型矿石小行星下降没有完成普通建筑移交。", () => ShipSnapshot(ship).ToString(Formatting.None));
        IsMigrationExpected = false;
        JObject landed = GroundBuildingSnapshot(ids);
        landed["flightHud"] = FlightHudSnapshot();
        var targets = new List<NaturalEntityTargetObservation>();
        NaturalEntityTargetObservation mine = default;
        bool found = false;
        yield return WaitUntil(() =>
        {
            NaturalEntityEcsService.QueryTargetObservations(CurrentPlayer(), 96f, targets);
            foreach (NaturalEntityTargetObservation candidate in targets)
                if (candidate.ItemId == "Mine_Iron" && candidate.HasHealth && candidate.Health.x > 0f)
                { mine = candidate; found = true; return true; }
            return false;
        }, 30d, "大型矿石小行星已落地，但加载地表没有正式铁矿山实体。");
        Vector2 minePoint = mine.AimBounds.center;
        Camera camera = Camera.main ?? throw new InvalidOperationException("采矿现场缺少玩家镜头。");
        Mod_PlayerTraits traits = CurrentPlayer().itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        if (traits == null || !traits.TryTeleportToScreenPosition(camera.WorldToScreenPoint(minePoint + Vector2.right)))
            throw new InvalidOperationException("正式GM入口不能将隔离玩家放到矿山旁。");
        float beforeOre = CountCurrentPickerInventoryItem("Ore_Iron");
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
        yield return WaitUntil(() => !NaturalEntityEcsService.Contains(mine.Handle) && CountCurrentPickerInventoryItem("Ore_Iron") > beforeOre,
            30d, "正式矿山伤害未移除矿山，或矿石没有经真实掉落拾取进入背包。");
        completed(new JObject
        {
            ["bodyId"] = body.BodyId, ["planetId"] = body.PlanetId, ["expectedWorld"] = expectedWorld,
            ["world"] = SceneManager.GetActiveScene().name, ["descending"] = descending, ["landed"] = landed,
            ["shipRemoved"] = session.GetShip(ship.ShipId) == null, ["flightRemoved"] = session.GetFlight(ship.ShipId) == null,
            ["mineFound"] = found, ["mineId"] = mine.ItemId, ["mineHealth"] = mine.Health.x,
            ["miningApplied"] = applied, ["mineRemoved"] = !NaturalEntityEcsService.Contains(mine.Handle),
            ["oreInventoryGain"] = CountCurrentPickerInventoryItem("Ore_Iron") - beforeOre,
            ["limitations"] = "正式测试船起飞、降落及普通建筑移交，正式矿山受击与掉矿入包；不覆盖手动飞行导航、镐子动画和真实联机。"
        });
    }
    #endregion
}
