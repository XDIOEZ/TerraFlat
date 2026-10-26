using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    #region 生产领域离线验证入口
    private static int groups;
    private static string root;
    private static string oreJson;
    private static JObject solar;
    private static SpaceOreAsteroidCatalog catalog;
    private static SpaceGameplaySettings settings;

    private static void Main(string[] args)
    {
        root = args.Single();
        oreJson = File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/Space/ore-asteroids.json"));
        solar = JObject.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Space/solar-system.json")));
        catalog = SpaceOreAsteroidCatalog.Parse(oreJson);
        settings = JsonConvert.DeserializeObject<SpaceGameplaySettings>(
            File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/Space/space-gameplay.json")));
        Run("小行星确定性、不同种子和跨星区身份", DeterministicFields);
        Run("正式矿物配置、数量与矿带边界", CompositionsAndFieldBounds);
        Run("非法矿带和矿物配置拒绝", RejectInvalidConfiguration);
        Run("非致命、致命准备失败与重复破坏", DamageAndAtomicDepletion);
        Run("受损和耗尽状态的重复保存恢复", PersistDamageAndDepletion);
        Run("三类大型小行星真实引力和降落边界", LargeAsteroidGravityAndLanding);
        Run("低重力下降有足够时间制动且不依赖向下推力", PoweredLowGravityDescent);
        Console.WriteLine($"PASS: {groups} ore asteroid regression groups; Unity, native physics, real pickups and live landing were not run.");
    }

    private static void Run(string name, Action test)
    { test(); groups++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double expected, double actual, string message, double tolerance = 1e-9d)
        => Check(Math.Abs(expected - actual) <= tolerance * Math.Max(1d, Math.Abs(expected)),
            $"{message}: expected={expected}, actual={actual}");
    private static UniverseState GenerateUniverse(int seed, SpaceSectorId sector)
    {
        var template = new UniverseState
        {
            TemplateId = solar.Value<string>("templateId"),
            FixedStepSeconds = solar.Value<double>("fixedStepSeconds"),
            MetersPerUnityUnit = solar.Value<double>("metersPerUnityUnit"),
            OrbitTimeScale = solar.Value<double>("orbitTimeScale"),
            Bodies = solar["bodies"].ToObject<List<BodyState>>()
        };
        return UniverseSectorFactory.Generate(template, seed, sector, solar.Value<bool>("randomizeBodyPlacements"));
    }
    private static List<SpaceOreAsteroidState> Asteroids(int seed, SpaceSectorId sector)
        => catalog.GenerateSectorOreAsteroids(seed, GenerateUniverse(seed, sector));
    private static string Encoded(object state) => JsonConvert.SerializeObject(state);
    #endregion

    #region 确定性矿带与参数拒绝
    private static void DeterministicFields()
    {
        List<SpaceOreAsteroidState> first = Asteroids(17413, default);
        List<SpaceOreAsteroidState> repeated = Asteroids(17413, default);
        Check(first.Count > 0 && Encoded(first) == Encoded(repeated), "same seed and sector must preserve full asteroid state");
        Check(Encoded(first) != Encoded(Asteroids(17414, default)), "another seed must produce different rock properties");
        Check(first.Select(value => value.AsteroidId).Distinct().Count() == first.Count, "each rock must have a unique identity");
        var remoteSector = new SpaceSectorId(9_007_199_254_740_993L, -9_007_199_254_740_992L);
        List<SpaceOreAsteroidState> remote = Asteroids(17413, remoteSector);
        Check(remote.Count == first.Count && remote.All(value => value.Sector == remoteSector), "large integer sectors must retain exact ownership");
        Check(!first.Select(value => value.AsteroidId).Intersect(remote.Select(value => value.AsteroidId)).Any(), "sectors cannot share rock identities");
        UniverseState reversed = GenerateUniverse(17413, default);
        reversed.Bodies.Reverse();
        var keyed = catalog.GenerateSectorOreAsteroids(17413, reversed).ToDictionary(value => value.AsteroidId, Encoded);
        Check(first.All(value => keyed[value.AsteroidId] == Encoded(value)), "body enumeration order cannot alter rock content");
        Check(catalog.GenerateSectorOreAsteroids(17413, new UniverseState()).Count == 0, "empty space cannot create rocks around nonexistent bodies");
    }

    private static void CompositionsAndFieldBounds()
    {
        UniverseState universe = GenerateUniverse(17413, default);
        List<SpaceOreAsteroidState> rocks = catalog.GenerateSectorOreAsteroids(17413, universe);
        var compositionIds = new HashSet<string>();
        int expectedCount = 0;
        foreach (SpaceOreAsteroidField field in catalog.Configuration.Fields)
        foreach (string centerId in field.CenterBodyIds)
        foreach (BodyState body in universe.Bodies.Where(value => value.TemplateBodyId == centerId))
        {
            expectedCount += field.CountPerBody;
            List<SpaceOreAsteroidState> selected = rocks.Where(value => value.CenterBodyId == body.BodyId &&
                value.AsteroidId.StartsWith(body.BodyId + "/ore:" + field.Id + ":", StringComparison.Ordinal)).ToList();
            Check(selected.Count == field.CountPerBody, "each configured center must receive the configured rock count");
            foreach (SpaceOreAsteroidState rock in selected)
            {
                SpaceOreAsteroidComposition composition = catalog.GetComposition(rock.CompositionId);
                compositionIds.Add(composition.ItemId);
                Check(rock.RadiusMeters >= field.MinimumRadiusMeters && rock.RadiusMeters <= field.MaximumRadiusMeters,
                    "radius must remain in its configured range");
                double clearance = rock.OffsetMeters.Magnitude - body.RadiusMeters - rock.RadiusMeters;
                Check(clearance >= field.MinimumClearanceMeters - 1e-7d && clearance <= field.MaximumClearanceMeters + 1e-7d,
                    "small rocks must lie outside the central body's surface clearance");
                Check(rock.OreAmount >= composition.AmountMin && rock.OreAmount <= composition.AmountMax &&
                    rock.StoneAmount >= field.StoneAmountMin && rock.StoneAmount <= field.StoneAmountMax,
                    "mineral and stone drops must stay in their independent ranges");
                Near(field.HitPointsPerRadius * rock.RadiusMeters, rock.HitPoints, "radius-derived health");
                Check(rock.IsAlive && !rock.Depleted && rock.HitPoints == rock.MaximumHitPoints, "new rocks must be intact");
            }
        }
        Check(rocks.Count == expectedCount, "no extra unconfigured rocks may be generated");
        Check(compositionIds.Count >= 3, "the normal world seed must expose at least three mineral kinds");
    }

    private static void RejectInvalidConfiguration()
    {
        void Reject(Action<JObject> change, string label)
        {
            JObject value = JObject.Parse(oreJson); change(value);
            bool rejected = false;
            try { _ = SpaceOreAsteroidCatalog.Parse(value.ToString()); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException)
            { rejected = true; }
            Check(rejected, "invalid configuration was accepted: " + label);
        }
        Reject(value => value["schemaVersion"] = 2, "version");
        Reject(value => value["unknownAsteroidParameter"] = 1, "unknown parameter");
        Reject(value => value["physicsActivationDistanceMeters"] = 0, "physics distance");
        Reject(value => value["fields"][0]["countPerBody"] = 0, "empty field");
        Reject(value => value["fields"][0]["minimumRadiusMeters"] = -1, "negative radius");
        Reject(value => value["fields"][0]["maximumRadiusMeters"] = 21, "oversized small rock");
        Reject(value => value["fields"][0]["centerBodyIds"] = new JArray("earth", "earth"), "repeated center");
        Reject(value => value["compositions"][0]["weight"] = 0, "zero mineral weight");
        Reject(value => value["compositions"][0]["amountMin"] = 0, "zero mineral drop");
        Reject(value => ((JArray)value["compositions"]).Add(value["compositions"][0].DeepClone()), "duplicate mineral id");
    }
    #endregion

    #region 伤害事务与持久化耗尽
    private static void DamageAndAtomicDepletion()
    {
        SpaceOreAsteroidState rock = Asteroids(17413, default).First();
        double start = rock.HitPoints;
        foreach (double damage in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(!rock.TryApplyResolvedDamage(damage, true, out double applied) && applied == 0d,
                "invalid damage must be rejected with no applied amount");
            Near(start, rock.HitPoints, "invalid attack health");
        }
        Check(rock.TryApplyResolvedDamage(start * .25d, false, out double firstApplied), "nonlethal damage does not need prepared loot");
        Near(start * .25d, firstApplied, "nonlethal applied amount");
        Near(start * .75d, rock.HitPoints, "nonlethal health");
        double remaining = rock.HitPoints;
        Check(!rock.TryApplyResolvedDamage(remaining, false, out double refused) && refused == 0d && rock.IsAlive && !rock.Depleted,
            "failed drop preparation must reject lethal damage atomically");
        Near(remaining, rock.HitPoints, "failed lethal preparation preserves health");
        Check(rock.TryApplyResolvedDamage(remaining * 2d, true, out double lethal), "prepared lethal attack must succeed");
        Near(remaining, lethal, "overkill only applies remaining health");
        Check(rock.HitPoints == 0d && rock.Depleted && !rock.IsAlive, "successful destruction must permanently mark depletion");
        for (int index = 0; index < 3; index++)
            Check(!rock.TryApplyResolvedDamage(start, true, out double repeated) && repeated == 0d,
                "destroyed rocks cannot accept another hit or emit duplicate drops");
    }

    private static void PersistDamageAndDepletion()
    {
        List<SpaceOreAsteroidState> rocks = Asteroids(17413, default);
        rocks[0].TryApplyResolvedDamage(rocks[0].HitPoints / 3d, false, out _);
        rocks[1].TryApplyResolvedDamage(rocks[1].HitPoints, true, out _);
        string expected = Encoded(rocks);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            rocks = JsonConvert.DeserializeObject<List<SpaceOreAsteroidState>>(Encoded(rocks));
            Check(Encoded(rocks) == expected, "asteroid identities, content and depletion must survive repeated saves");
            Check(rocks[0].IsAlive && rocks[0].HitPoints < rocks[0].MaximumHitPoints, "saved partial damage cannot heal");
            Check(rocks[1].Depleted && !rocks[1].TryApplyResolvedDamage(1d, true, out double amount) && amount == 0d,
                "saved depleted rock cannot resume producing minerals");
        }
    }
    #endregion

    #region 大型小行星真实领域适配
    private static void LargeAsteroidGravityAndLanding()
    {
        UniverseState universe = GenerateUniverse(17413, default);
        var simulation = new UniverseSimulation(universe);
        List<BodyState> bodies = universe.Bodies.Where(body => body.SurfaceProfileId?.StartsWith("core:surface.asteroid.",
            StringComparison.Ordinal) == true).ToList();
        Check(bodies.Count >= 3, "the built-in universe must contain iron, copper and rare large asteroids");
        foreach (BodyState body in bodies)
        {
            var gravity = new SpaceGravitySource(simulation, body, settings);
            Check(gravity.RadiusMeters > body.RadiusMeters && gravity.DampingPerSecond == 0d,
                "large airless rocks must have a valid capture field without atmosphere drag");
            Check(body.SurfaceGravity >= settings.MinimumGravityForWalking && body.SurfaceGravity < 1d,
                "large asteroids must retain low-gravity walking eligibility");
            double shipRadius = 8d, reach = Math.Max(100d, body.RadiusMeters * .25d);
            SpaceVector2 near = body.PositionMeters + new SpaceVector2(body.RadiusMeters + shipRadius + reach - .001d, 0d);
            SpaceVector2 far = body.PositionMeters + new SpaceVector2(body.RadiusMeters + shipRadius + reach + .001d, 0d);
            Check(ShipLandingGuidance.FindNearbyBody(near, shipRadius, new[] { body }) == body,
                "ship landing must recognize an asteroid inside the normal approach range");
            Check(ShipLandingGuidance.FindNearbyBody(far, shipRadius, new[] { body }) == null,
                "ship landing must reject the same asteroid outside the normal approach range");
            SurfaceContact contact = ShipLandingGuidance.CreateDescentContact(body, near,
                body.VelocityMetersPerSecond, universe.SimulationSeconds);
            Check(contact.IsValid && contact.BodyId == body.BodyId && contact.PlanetId == body.PlanetId &&
                contact.Sector == body.Sector && contact.RelativeVelocityMetersPerSecond.Magnitude < 1e-9d,
                "descent contact must preserve the asteroid's independent world and parent velocity");
            SpaceMotionState entry = ShipLandingGuidance.CreateParkedSpaceEntry(simulation, body, .4d, shipRadius, settings);
            Near(body.RadiusMeters + shipRadius + settings.SpaceEntryClearanceMeters,
                (entry.PositionMeters - body.PositionMeters).Magnitude, "post-takeoff clearance");
            Check(entry.VelocityMetersPerSecond == body.VelocityMetersPerSecond, "takeoff must inherit the real asteroid velocity");
        }
    }

    private static void PoweredLowGravityDescent()
    {
        Near(settings.LandingDurationSeconds,
            ShipLandingGuidance.ResolvePoweredLandingDurationSeconds(settings.EarthGravity, settings),
            "Earth retains its configured landing duration");
        foreach (BodyState body in GenerateUniverse(17413, default).Bodies.Where(value =>
            value.SurfaceProfileId?.StartsWith("core:surface.asteroid.", StringComparison.Ordinal) == true))
        {
            double duration = ShipLandingGuidance.ResolvePoweredLandingDurationSeconds(body.SurfaceGravity, settings);
            Check(double.IsFinite(duration) && duration > settings.LandingDurationSeconds,
                "low-gravity descent must allow enough time to fall with no downward engine");
            double height = settings.SurfaceEntryHeightMeters, speed = 0d, elapsed = 0d;
            const double mass = 1000d;
            for (int step = 0; step < 10000 && elapsed < duration; step++)
            {
                double seconds = Math.Min(.02d, duration - elapsed);
                if (seconds < 1e-10d) break;
                double desired = ShipLandingGuidance.LandingSpeed(elapsed, height, seconds, settings, body.SurfaceGravity);
                double requested = (body.SurfaceGravity + (desired - speed) / seconds) * mass;
                Check(requested >= -1e-5d, "guidance cannot require physically unavailable negative vertical thrust");
                double thrust = ShipLandingGuidance.RequiredVerticalThrust(mass, body.SurfaceGravity, speed, desired, seconds);
                speed += (thrust / mass - body.SurfaceGravity) * seconds;
                height += speed * seconds;
                elapsed += seconds;
                Check(height >= -1e-7d && double.IsFinite(speed), "powered descent cannot overshoot below the surface");
            }
            Near(0d, height, "landing reaches the surface", 1e-6d);
            Check(Math.Abs(speed) <= settings.MaximumSafeLandingSpeedMetersPerSecond,
                "powered low-gravity touchdown must stay within the real safe impact speed");
        }
    }
    #endregion
}
