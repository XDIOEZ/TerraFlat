using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 矿石小行星独立规则采样
    private static void RegisterOreAsteroidOperations(Dictionary<string, Func<JObject, JObject>> target)
    {
        target.Add("space/ore_asteroid_rules", CaptureSmallOreAsteroidRules);
        target.Add("space/ore_asteroid_profiles", CaptureLargeOreAsteroidProfiles);
    }

    // 独立目录和状态调用正式生成、伤害与保存接口，Lua 持有业务断言。
    private static JObject CaptureSmallOreAsteroidRules(JObject args)
    {
        const int seed = 17413;
        SpaceOreAsteroidCatalog catalog = SpaceOreAsteroidCatalog.LoadDefault();
        SpaceCatalog universeCatalog = SpaceCatalog.LoadDefault();
        UniverseState universe = universeCatalog.GenerateSector(seed, default);
        List<SpaceOreAsteroidState> first = catalog.GenerateSectorOreAsteroids(seed, universe);
        List<SpaceOreAsteroidState> repeated = catalog.GenerateSectorOreAsteroids(seed, universe);
        var definitions = MineralContentDefinitions();
        var compositions = new JArray();
        foreach (SpaceOreAsteroidComposition composition in catalog.Configuration.Compositions)
        {
            ItemDefinitionDto definition = definitions[composition.ItemId];
            compositions.Add(new JObject
            {
                ["id"] = composition.Id, ["itemId"] = composition.ItemId, ["weight"] = composition.Weight,
                ["amountMin"] = composition.AmountMin, ["amountMax"] = composition.AmountMax,
                ["stackable"] = definition.Stackable, ["canBePickedUp"] = definition.CanBePickedUp ?? true,
                ["spriteAddress"] = definition.Visual?.SpriteAddress
            });
        }
        var fields = new JArray();
        foreach (SpaceOreAsteroidField field in catalog.Configuration.Fields)
        {
            var centers = new JArray();
            foreach (BodyState body in universe.Bodies)
                if (field.CenterBodyIds.Contains(body.TemplateBodyId ?? body.BodyId, StringComparer.Ordinal))
                    centers.Add(new JObject { ["id"] = body.BodyId, ["radius"] = body.RadiusMeters });
            fields.Add(new JObject
            {
                ["id"] = field.Id, ["centers"] = centers, ["countPerBody"] = field.CountPerBody,
                ["minRadius"] = field.MinimumRadiusMeters, ["maxRadius"] = field.MaximumRadiusMeters,
                ["minClearance"] = field.MinimumClearanceMeters, ["maxClearance"] = field.MaximumClearanceMeters,
                ["hitPointsPerRadius"] = field.HitPointsPerRadius,
                ["stoneMin"] = field.StoneAmountMin, ["stoneMax"] = field.StoneAmountMax
            });
        }
        SpaceOreAsteroidState damaged = JsonConvert.DeserializeObject<SpaceOreAsteroidState>(JsonConvert.SerializeObject(first[0]));
        var damage = new JArray();
        void Hit(string label, double amount, bool prepared)
        {
            double before = damaged.HitPoints;
            bool accepted = damaged.TryApplyResolvedDamage(amount, prepared, out double applied);
            damage.Add(new JObject { ["label"] = label, ["before"] = before, ["after"] = damaged.HitPoints,
                ["accepted"] = accepted, ["applied"] = applied, ["alive"] = damaged.IsAlive, ["depleted"] = damaged.Depleted });
        }
        Hit("zero", 0d, true); Hit("negative", -1d, true); Hit("nan", double.NaN, true);
        Hit("infinity", double.PositiveInfinity, true);
        Hit("nonlethal", damaged.HitPoints * .25d, false);
        Hit("unprepared_lethal", damaged.HitPoints, false);
        Hit("prepared_lethal", damaged.HitPoints * 2d, true);
        Hit("repeated_lethal", damaged.MaximumHitPoints, true);
        string saved = JsonConvert.SerializeObject(damaged);
        SpaceOreAsteroidState restored = JsonConvert.DeserializeObject<SpaceOreAsteroidState>(saved);
        bool restoredHit = restored.TryApplyResolvedDamage(1d, true, out double restoredApplied);
        var remoteSector = new SpaceSectorId(9007199254740993L, -9007199254740992L);
        UniverseState remote = UniverseSectorFactory.Generate(new UniverseState
            { Bodies = new List<BodyState>(universeCatalog.Bodies) }, seed, remoteSector, false);
        return new JObject
        {
            ["scope"] = "isolated-production-asteroid-domain", ["seed"] = seed,
            ["fields"] = fields, ["compositions"] = compositions,
            ["first"] = JArray.FromObject(first), ["repeat"] = JArray.FromObject(repeated),
            ["alternate"] = JArray.FromObject(catalog.GenerateSectorOreAsteroids(seed + 1, universe)),
            ["remote"] = JArray.FromObject(catalog.GenerateSectorOreAsteroids(seed, remote)),
            ["emptyCount"] = catalog.GenerateSectorOreAsteroids(seed, new UniverseState()).Count,
            ["damage"] = damage,
            ["saved"] = JObject.Parse(saved), ["restored"] = JObject.FromObject(restored),
            ["restoredHit"] = restoredHit, ["restoredApplied"] = restoredApplied,
            ["guards"] = CaptureInvalidOreAsteroidConfiguration(catalog)
        };
    }

    private static JArray CaptureInvalidOreAsteroidConfiguration(SpaceOreAsteroidCatalog catalog)
    {
        var result = new JArray();
        void Guard(string label, Action<JObject> change)
        {
            JObject json = JObject.FromObject(catalog.Configuration); change(json);
            string exception = null;
            try { _ = SpaceOreAsteroidCatalog.Parse(json.ToString()); }
            catch (Exception error) { exception = error.GetType().Name; }
            result.Add(new JObject { ["label"] = label, ["exception"] = exception });
        }
        Guard("schema", json => json["SchemaVersion"] = 2);
        Guard("unknown_field", json => json["UnknownField"] = 1);
        Guard("physics_distance", json => json["PhysicsActivationDistanceMeters"] = 0);
        Guard("field_count", json => json["Fields"][0]["CountPerBody"] = 0);
        Guard("radius", json => json["Fields"][0]["MaximumRadiusMeters"] = 21);
        Guard("duplicate_center", json => json["Fields"][0]["CenterBodyIds"] = new JArray("earth", "earth"));
        Guard("mineral_weight", json => json["Compositions"][0]["Weight"] = 0);
        Guard("mineral_amount", json => json["Compositions"][0]["AmountMin"] = 0);
        Guard("duplicate_composition", json => ((JArray)json["Compositions"]).Add(json["Compositions"][0].DeepClone()));
        return result;
    }
    #endregion

    #region 大型矿石小行星目录与真实矿山
    private static JObject CaptureLargeOreAsteroidProfiles(JObject args)
    {
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        UniverseState universe = catalog.GenerateSector(17413, default);
        var simulation = new UniverseSimulation(universe);
        var minerals = MineralContentDefinitions();
        NaturalGenerationRuleCatalog natural = NaturalGenerationRuleCatalogLoader.LoadBuiltIn();
        IReadOnlyList<RuntimeLootTable> loot = LootTableCatalogLoader.LoadBuiltIn();
        var rows = new JArray();
        foreach (BodyState body in universe.Bodies)
        {
            if (!body.SurfaceProfileId.StartsWith("core:surface.asteroid.", StringComparison.Ordinal)) continue;
            SurfaceProfile surface = catalog.GetSurfaceProfile(body.BodyId);
            PlanetData planet = catalog.CreatePlanetData(body.BodyId, new PlanetData { Radius = 2048 });
            var resourceRows = new JArray();
            foreach (string id in surface.EcologyRuleIds)
            {
                FlatWorld.WorldModel.EcologySpawnRuleSnapshot rule = natural.GetEcologyRule(id);
                ItemDefinitionDto definition = minerals[rule.ItemId];
                var drops = new JArray();
                if (!string.IsNullOrWhiteSpace(definition.LootTableId))
                    foreach (RuntimeLootTableEntry entry in loot.First(table => table.Id == definition.LootTableId).Entries)
                        drops.Add(new JObject { ["itemId"] = entry.ItemId, ["chance"] = entry.Probability,
                            ["min"] = entry.MinAmount, ["max"] = entry.MaxAmount,
                            ["canBePickedUp"] = minerals[entry.ItemId].CanBePickedUp ?? true });
                resourceRows.Add(new JObject { ["ruleId"] = rule.RuleId, ["itemId"] = rule.ItemId,
                    ["spawnChance"] = rule.SpawnChance, ["multiplier"] = rule.SpawnChanceMultiplier,
                    ["spriteAddress"] = definition.Visual?.SpriteAddress, ["loot"] = drops });
            }
            var gravity = new SpaceGravitySource(simulation, body, SpaceGameplaySettings.Current);
            double reach = Math.Max(100d, body.RadiusMeters * .25d), shipRadius = 8d;
            SpaceVector2 near = body.PositionMeters + new SpaceVector2(body.RadiusMeters + shipRadius + reach - .001d, 0d);
            SpaceVector2 far = body.PositionMeters + new SpaceVector2(body.RadiusMeters + shipRadius + reach + .001d, 0d);
            SurfaceContact contact = ShipLandingGuidance.CreateDescentContact(body, near,
                body.VelocityMetersPerSecond, universe.SimulationSeconds);
            rows.Add(new JObject
            {
                ["id"] = body.BodyId, ["templateId"] = body.TemplateBodyId, ["planetId"] = body.PlanetId,
                ["surfaceId"] = surface.Id, ["configuredRadius"] = surface.SurfaceRadiusTiles,
                ["planetRadius"] = planet.Radius, ["topology"] = planet.TopologyMode.ToString(),
                ["bodyRadius"] = body.RadiusMeters, ["gravity"] = body.SurfaceGravity,
                ["captureRadius"] = gravity.RadiusMeters, ["damping"] = gravity.DampingPerSecond,
                ["pressure"] = AtmosphereService.PressureKPa(planet.Atmosphere),
                ["numbers"] = JObject.FromObject(surface.NumericParameters), ["tiles"] = JObject.FromObject(surface.TileParameters),
                ["resources"] = resourceRows,
                ["nearLanding"] = ShipLandingGuidance.FindNearbyBody(near, shipRadius, new[] { body })?.BodyId,
                ["farLanding"] = ShipLandingGuidance.FindNearbyBody(far, shipRadius, new[] { body })?.BodyId,
                ["contactBody"] = contact.BodyId, ["contactPlanet"] = contact.PlanetId,
                ["relativeSpeed"] = contact.RelativeVelocityMetersPerSecond.Magnitude,
                ["poweredDescent"] = CaptureOreAsteroidPoweredDescent(body.SurfaceGravity)
            });
        }
        UniverseState reload = catalog.GenerateSector(17413, default);
        reload.SimulationSeconds = 123.45d;
        reload.Bodies.RemoveAll(body => body.SurfaceProfileId.StartsWith("core:surface.asteroid.", StringComparison.Ordinal));
        var preserved = reload.Bodies.ToDictionary(body => body.BodyId,
            body => (body.Seed, body.PlacementAngleRadians, body.PlanetId));
        catalog.RebindUniverse(reload);
        int afterFirst = reload.Bodies.Count;
        catalog.RebindUniverse(reload);
        bool originalsPreserved = preserved.All(pair => reload.Bodies.Any(body => body.BodyId == pair.Key &&
            body.Seed == pair.Value.Seed && body.PlacementAngleRadians == pair.Value.PlacementAngleRadians &&
            body.PlanetId == pair.Value.PlanetId));
        return new JObject { ["scope"] = "isolated-production-asteroid-profiles", ["asteroids"] = rows,
            ["earthLandingDuration"] = ShipLandingGuidance.ResolvePoweredLandingDurationSeconds(
                SpaceGameplaySettings.Current.EarthGravity, SpaceGameplaySettings.Current),
            ["rebind"] = new JObject { ["expected"] = universe.Bodies.Count, ["afterFirst"] = afterFirst,
                ["afterSecond"] = reload.Bodies.Count, ["simulationSeconds"] = reload.SimulationSeconds,
                ["originalsPreserved"] = originalsPreserved } };
    }

    private static JObject CaptureOreAsteroidPoweredDescent(double gravity)
    {
        SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
        double duration = ShipLandingGuidance.ResolvePoweredLandingDurationSeconds(gravity, settings);
        double height = settings.SurfaceEntryHeightMeters, speed = 0d, elapsed = 0d;
        double minimumRequestedThrust = double.PositiveInfinity;
        const double mass = 1000d;
        for (int step = 0; step < 10000 && elapsed < duration; step++)
        {
            double seconds = Math.Min(.02d, duration - elapsed);
            if (seconds < 1e-10d) break;
            double desired = ShipLandingGuidance.LandingSpeed(elapsed, height, seconds, settings, gravity);
            minimumRequestedThrust = Math.Min(minimumRequestedThrust,
                (gravity + (desired - speed) / seconds) * mass);
            double thrust = ShipLandingGuidance.RequiredVerticalThrust(mass, gravity, speed, desired, seconds);
            speed += (thrust / mass - gravity) * seconds;
            height += speed * seconds;
            elapsed += seconds;
        }
        return new JObject { ["duration"] = duration, ["elapsed"] = elapsed, ["height"] = height,
            ["speed"] = speed, ["minimumRequestedThrust"] = minimumRequestedThrust,
            ["source"] = "production-guidance-independent-vertical-integration" };
    }
    #endregion
}
