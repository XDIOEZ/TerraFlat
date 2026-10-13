using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using FlatWorld.Structures;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>太空集成用例通过正式入口准备条件，结果交给 Lua 断言。</summary>
public sealed class GameplayTestSpaceScenario : IDisposable
{
    #region 操作契约与隔离状态
    public static bool IsMigrationExpected { get; private set; }
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<ChunkLease> leases = new();
    private SpaceSession session;
    private string profile;
    private global::WorldAddress home;
    private Vector2 homePosition;
    private bool homeChanged;
    private bool initialInvincibility;
    private bool protectionChanged;
    private string controlledConsole;
    private readonly List<(ShipState Ship, ShipPieceState Piece)> contactFixtures = new();
    private bool disposed;

    public static bool TryValidate(string operation, string argument, out string error)
    {
        error = null;
        bool supported = GameplayTestUniverseProbe.Supports(operation) || GameplayTestShipProbe.Supports(operation) ||
            operation is "session" or "ship_templates" or "ship_flight" or "ship_collision" or "sector_transition" or "space_visit";
        if (!supported) error = "未开放的太空测试操作：" + operation;
        else if (operation == "sector_transition")
        {
            if (argument is not ("right" or "left" or "up" or "down" or "corner"))
                error = "sector_transition 需要 right/left/up/down/corner。";
        }
        else if (!string.IsNullOrEmpty(argument)) error = operation + " 不接受参数。";
        return error == null;
    }

    public IEnumerator Execute(string operation, string argument, Action<JObject> completed)
    {
        if (!TryValidate(operation, argument, out string error)) throw new ArgumentException(error);
        if (disposed || GameNetwork.IsOnline || !GameNetwork.HasStateAuthority || Time.timeScale <= 0f)
            throw new InvalidOperationException("太空集成测试需要未暂停的单机权威测试世界。");
        Player player = CurrentPlayer();
        profile = player.ProfileName;
        home = global::WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        homePosition = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        session = SpaceSession.EnsureLoaded();
        if (session?.State == null) throw new InvalidOperationException("正式太空会话没有载入。");
        JObject sample = null;
        Exception failure = null;
        IEnumerator flow = operation switch
        {
            "session" => ObserveSession(value => sample = value),
            "ship_templates" => ReadTemplates(value => sample = value),
            "ship_flight" => FlyTemplates(value => sample = value),
            "ship_collision" => CollideProductionFloors(value => sample = value),
            "space_visit" => VisitConfiguredSectors(value => sample = value),
            "sector_transition" => CrossSector(argument, value => sample = value),
            _ => throw new ArgumentException("操作不是会话测试：" + operation)
        };
        try
        {
            yield return Advance(flow, exception => failure = exception);
            if (homeChanged && Application.isPlaying)
                yield return Advance(ReturnHome(), exception => failure = failure == null ? exception :
                    new AggregateException("用例失败且恢复初始测试世界失败。", failure, exception));
            if (failure != null) throw failure;
            completed(sample ?? throw new InvalidOperationException("会话用例没有结果。"));
        }
        finally { Dispose(); }
    }
    #endregion

    #region 真实固定步的跨区船体接触
    private IEnumerator CollideProductionFloors(Action<JObject> completed)
    {
        if (session.IsSpaceView) throw new InvalidOperationException("船体接触夹具需要从隔离地表入口创建。");
        IReadOnlyList<FixedStructureDefinition> templates = null;
        Exception error = null;
        yield return FixedStructureCatalog.LoadAsync(value => templates = value, value => error = value);
        if (error != null) throw error;
        string floorId = null;
        foreach (FixedStructureDefinition template in templates)
        foreach (FixedStructureMember member in template.Members)
        {
            ItemData candidate = GameRes.ExistingInstance.CreateItemData(member.ItemId);
            if (Mod_ShipPart.Read(candidate)?.Kind == ShipPieceKind.Floor) { floorId = member.ItemId; break; }
        }
        if (floorId == null) throw new InvalidOperationException("正式模板目录没有可用于接触的地板物品。");
        SpaceSectorId source = FindEmptySector(), adjacent = new(source.X + 1, source.Y);
        ShipState left = AddContactFixture(floorId, 100d), right = AddContactFixture(floorId, 200d);
        ShipState remote = AddContactFixture(floorId, 300d);
        double leftWidth = left.Pieces[0].Width * left.CellSizeMeters;
        double rightWidth = right.Pieces[0].Width * right.CellSizeMeters;
        SetContactFixturePose(left, source, new SpaceVector2(SpaceSectorCoordinates.HalfSizeMeters - leftWidth - 1d, 0d), new SpaceVector2(1d, 0d));
        SetContactFixturePose(right, adjacent, new SpaceVector2(-SpaceSectorCoordinates.HalfSizeMeters + 1d, 0d), new SpaceVector2(-1d, 0d));
        SetContactFixturePose(remote, new SpaceSectorId(source.X + 1000, source.Y), new SpaceVector2(0d, 0d), new SpaceVector2(1d, 0d));
        JObject beforeLeft = ShipSnapshot(left), beforeRight = ShipSnapshot(right), beforeRemote = ShipSnapshot(remote);
        double beforeTime = session.State.SimulationSeconds;
        yield return Wait(3d);
        completed(new JObject { ["scope"] = "live-fixed-step-contacts", ["fixtureSetup"] = "production-floor-transactions",
            ["beforeLeft"] = beforeLeft, ["beforeRight"] = beforeRight, ["beforeRemote"] = beforeRemote,
            ["afterLeft"] = ShipSnapshot(left), ["afterRight"] = ShipSnapshot(right), ["afterRemote"] = ShipSnapshot(remote),
            ["beforeTime"] = beforeTime, ["afterTime"] = session.State.SimulationSeconds,
            ["rightWidth"] = rightWidth, ["source"] = Sector(source), ["adjacent"] = Sector(adjacent) });
    }

    private ShipState AddContactFixture(string floorId, double offset)
    {
        ItemData data = GameRes.ExistingInstance.CreateItemData(floorId);
        data.Stack.Amount = 1f;
        Mod_Building.SetInstalledDataState(data);
        ShipPieceState piece = session.AddPiece(data,
            WorldLocalPresentation.ProjectPosition(homePosition + new Vector2((float)offset, 0f)), 0, out ShipState ship);
        contactFixtures.Add((ship, piece));
        session.CommitPiece(ship, piece);
        // 夹具只提高自有地板耐久以隔离冲量规则，避免接触后结构销毁干扰质量断言。
        piece.Health = piece.MaxHealth = 1e12d;
        return ship;
    }

    private void SetContactFixturePose(ShipState ship, SpaceSectorId sector, SpaceVector2 position, SpaceVector2 velocity)
    {
        ShipFlightState flight = session.GetFlight(ship.ShipId);
        flight.Phase = ShipFlightPhase.Cruising;
        ship.PlatformKind = ShipPlatformKind.Space;
        SetOwnedShipPose(ship, flight, sector, position, velocity);
    }
    #endregion

    #region 真实会话与配置星区
    private IEnumerator ObserveSession(Action<JObject> completed)
    {
        var before = SessionSnapshot();
        yield return Wait(0.25d);
        SpaceSession.Capture(SaveDataMgr.Instance.SaveData);
        SpaceSessionState restored = SaveDataMgr.DeserializeSpaceState(SaveDataMgr.Instance.SaveData.SpaceStateJson);
        completed(new JObject { ["scope"] = "live-session", ["before"] = before,
            ["after"] = SessionSnapshot(), ["savedVersion"] = restored.Version,
            ["savedTime"] = restored.SimulationSeconds, ["savedSectorCount"] = restored.Sectors.Count });
    }

    private IEnumerator ReadTemplates(Action<JObject> completed)
    {
        IReadOnlyList<FixedStructureDefinition> templates = null;
        Exception error = null;
        yield return FixedStructureCatalog.LoadAsync(value => templates = value, value => error = value);
        if (error != null) throw error;
        var entries = new JArray();
        foreach (FixedStructureDefinition template in templates)
        {
            if (!string.Equals(template.Kind, "ship", StringComparison.OrdinalIgnoreCase)) continue;
            var members = new JArray();
            foreach (FixedStructureMember member in template.Members)
            {
                ItemData data = GameRes.ExistingInstance.CreateItemData(member.ItemId);
                ShipPartConfiguration part = data == null ? null : Mod_ShipPart.Read(data);
                members.Add(new JObject { ["id"] = member.MemberId, ["itemId"] = member.ItemId,
                    ["registered"] = data != null, ["kind"] = part?.Kind.ToString(),
                    ["x"] = member.X, ["y"] = member.Y, ["quarterTurns"] = member.QuarterTurns });
            }
            entries.Add(new JObject { ["id"] = template.Id, ["width"] = template.Width,
                ["height"] = template.Height, ["members"] = members });
        }
        completed(new JObject { ["scope"] = "live-content", ["templates"] = entries });
    }

    private IEnumerator VisitConfiguredSectors(Action<JObject> completed)
    {
        ProtectPlayer();
        var visits = new JArray();
        foreach (SpaceSectorId sector in SpaceCatalog.LoadDefault().ConfiguredSectors)
        {
            UniverseSimulation universe = session.GetUniverse(sector);
            SpaceVector2 point = FindEmptyPoint(universe);
            yield return Teleport(new SpaceMotionState { Sector = sector, PositionMeters = point, RadiusMeters = .3d });
            double deadline = Time.realtimeSinceStartupAsDouble + 30d;
            while (!HasAllPlanetViews(universe) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            var bodies = new JArray();
            foreach (BodyState body in universe.State.Bodies)
            {
                Planet view = SpaceMgr.ExistingInstance?.FindPlanetByBodyId(body.BodyId);
                bool registered = SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet);
                bodies.Add(new JObject { ["id"] = body.BodyId, ["planetId"] = body.PlanetId,
                    ["planetRegistered"] = registered, ["registeredBodyId"] = registered ? planet.BodyId : null,
                    ["viewReady"] = view != null, ["viewBodyId"] = view?.planetData?.BodyId,
                    ["projected"] = Vector(session.ProjectPosition(new SpaceSectorPosition(body.Sector, body.PositionMeters))),
                    ["viewPosition"] = view == null ? null : Vector(view.transform.position) });
            }
            visits.Add(new JObject { ["sector"] = Sector(sector), ["state"] = SessionSnapshot(), ["bodies"] = bodies });
        }
        completed(new JObject { ["scope"] = "live-presentation", ["visits"] = visits });
    }

    private static bool HasAllPlanetViews(UniverseSimulation universe)
    {
        foreach (BodyState body in universe.State.Bodies)
            if (SpaceMgr.ExistingInstance?.FindPlanetByBodyId(body.BodyId) == null) return false;
        return true;
    }

    private static SpaceVector2 FindEmptyPoint(UniverseSimulation universe)
    {
        foreach (SpaceVector2 point in new[] { new SpaceVector2(100000d, 100000d), new SpaceVector2(-100000d, 100000d),
                     new SpaceVector2(100000d, -100000d), new SpaceVector2(-100000d, -100000d) })
        {
            bool clear = true;
            foreach (BodyState body in universe.State.Bodies)
                if ((point - body.PositionMeters).Magnitude <= body.RadiusMeters + 100d) { clear = false; break; }
            if (clear) return point;
        }
        throw new InvalidOperationException("配置星区没有找到安全的测试入场点。");
    }
    #endregion

    #region 真实星区越界与折返
    private IEnumerator CrossSector(string direction, Action<JObject> completed)
    {
        ProtectPlayer();
        int dx = direction is "right" or "corner" ? 1 : direction == "left" ? -1 : 0;
        int dy = direction is "up" or "corner" ? 1 : direction == "down" ? -1 : 0;
        SpaceSectorId source = FindEmptySector();
        SpaceSectorId destination = new(source.X + dx, source.Y + dy);
        SpaceVector2 velocity = new(dx * 512d, dy * 512d);
        double edge = SpaceSectorCoordinates.HalfSizeMeters - 2048d;
        var startPosition = new SpaceVector2(dx * edge, dy * edge);
        yield return Teleport(new SpaceMotionState { Sector = source, PositionMeters = startPosition, RadiusMeters = .3d });
        yield return Teleport(new SpaceMotionState { Sector = source, PositionMeters = startPosition,
            VelocityMetersPerSecond = velocity, RadiusMeters = .3d });
        JObject before = SessionSnapshot();
        uint revision = session.GetPassenger(CurrentPlayer()).RealmRevision;
        yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(destination) &&
                session.GetPassengerPosition(session.GetPassenger(CurrentPlayer())).Sector == destination,
            15d, "玩家没有按真实速度越过目标星区边界。");
        JObject crossed = SessionSnapshot();
        yield return WaitUntil(() => !Contains(session.RequiredSectors, source), 20d, "越过过渡带后源星区租约未释放。");
        JObject settled = SessionSnapshot();
        yield return Teleport(new SpaceMotionState { Sector = destination,
            PositionMeters = new SpaceVector2(-dx * edge, -dy * edge), VelocityMetersPerSecond = velocity * -1d, RadiusMeters = .3d });
        yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(source) &&
                session.GetPassengerPosition(session.GetPassenger(CurrentPlayer())).Sector == source,
            15d, "玩家未能自然折返源星区。");
        JObject returned = SessionSnapshot();
        completed(new JObject { ["scope"] = "live-sector-transition", ["direction"] = direction,
            ["source"] = Sector(source), ["destination"] = Sector(destination), ["before"] = before,
            ["crossed"] = crossed, ["settled"] = settled, ["returned"] = returned,
            ["realmRevisionBefore"] = revision });
    }

    private SpaceSectorId FindEmptySector()
    {
        for (long x = 2; x < 100; x++)
        {
            bool clear = true;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (SpaceCatalog.LoadDefault().GenerateSector(SaveDataMgr.Instance.SaveData.Seed,
                        new SpaceSectorId(x + dx, 2 + dy)).Bodies.Count != 0) clear = false;
            if (clear) return new SpaceSectorId(x, 2);
        }
        throw new InvalidOperationException("无法找到不含星体的九宫格过渡测试区。");
    }
    #endregion

    #region 模板飞船起飞与导航降落
    private IEnumerator FlyTemplates(Action<JObject> completed)
    {
        if (session.IsSpaceView) throw new InvalidOperationException("模板起飞测试必须从隔离地表世界开始。");
        ProtectPlayer();
        IReadOnlyList<FixedStructureDefinition> templates = null;
        Exception loadError = null;
        yield return FixedStructureCatalog.LoadAsync(value => templates = value, error => loadError = error);
        if (loadError != null) throw loadError;
        var results = new JArray();
        foreach (FixedStructureDefinition template in templates)
        {
            if (!string.Equals(template.Kind, "ship", StringComparison.OrdinalIgnoreCase)) continue;
            yield return PrepareSite(template);
            if (!ShipFixedStructureBuilder.TryBuild(CurrentPlayer(), template, out ShipState ship, out string reason))
                throw new InvalidOperationException(reason);
            homeChanged = true;
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            string console = null;
            while (console == null && Time.realtimeSinceStartupAsDouble < deadline)
            { console = session.ResolveConsole(ship.ShipId); yield return null; }
            if (console == null) throw new InvalidOperationException("真实模板船的控制台未获得供电。");
            ShipPieceState consolePiece = ship.Pieces.Find(piece => piece.PieceId == console);
            Player player = CurrentPlayer();
            Camera camera = Camera.main ?? throw new InvalidOperationException("测试玩家没有镜头。");
            Vector2 position = session.DisplayPoint(ship, consolePiece.CellX + .5d, consolePiece.CellY + .5d);
            Mod_PlayerTraits traits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
            if (traits == null || !traits.TryTeleportToScreenPosition(camera.WorldToScreenPoint(position)))
                throw new InvalidOperationException("正式 GM 入口无法准备控制台旁的玩家。");
            yield return Wait(.25d);
            bool entered = session.EnterConsole(CurrentPlayer(), console);
            if (!entered) throw new InvalidOperationException("正式控制台拒绝进入驾驶。");
            JObject landed = ShipSnapshot(ship);
            if (!ShipFixedStructureBuilder.TryRefill(CurrentPlayer(), templates, out reason))
                throw new InvalidOperationException(reason);
            JObject filled = ShipSnapshot(ship);
            IsMigrationExpected = true;
            if (!session.ToggleIgnition(console)) throw new InvalidOperationException("真实模板船点火被拒绝。");
            JObject lifting = ShipSnapshot(ship);
            yield return WaitUntil(() => session.GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising && Ready(),
                90d, "飞船没有完成正式起飞和太空迁移。");
            IsMigrationExpected = false;
            ProtectCurrentPlayer();
            JObject cruising = ShipSnapshot(ship);
            session.ExitConsole(CurrentPlayer());
            controlledConsole = console;
            session.SetControl(console, Vector2.up, 0f);
            yield return Wait(.5d);
            JObject thrust = ShipSnapshot(ship);
            session.ClearControl(console);
            yield return Wait(.5d);
            JObject coast = ShipSnapshot(ship);
            session.SetControl(console, Vector2.zero, 1f);
            yield return Wait(.25d);
            JObject turned = ShipSnapshot(ship);
            session.ClearControl(console);
            var target = SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(ship.PositionX + 15d, ship.PositionY));
            bool navigationAccepted = session.StartNavigation(console, target);
            if (!navigationAccepted) throw new InvalidOperationException("真实导航器供电或光纤连接未就绪。");
            yield return WaitUntil(() => session.GetLatestNavigation(ship.ShipId)?.Active == false, 45d,
                "真实导航没有完成到点和制动。");
            JObject navigated = ShipSnapshot(ship);
            double distance = SpaceSectorCoordinates.Difference(
                new SpaceSectorPosition(ship.Sector, new SpaceVector2(ship.PositionX, ship.PositionY)), target).Magnitude;
            JObject shipTransition = null;
            yield return CrossOwnedShip(ship, value => shipTransition = value);
            bool landingAccepted = session.RequestLanding(console);
            if (!landingAccepted) throw new InvalidOperationException("飞船已离开可降落范围，正式降落入口拒绝了请求。");
            IsMigrationExpected = true;
            yield return WaitUntil(() => (session.GetFlight(ship.ShipId)?.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating) && Ready(),
                90d, "飞船未完成正式降落。");
            IsMigrationExpected = false;
            JObject landedAgain = ShipSnapshot(ship);
            results.Add(new JObject { ["templateId"] = template.Id, ["enteredConsole"] = entered,
                ["landed"] = landed, ["filled"] = filled, ["lifting"] = lifting, ["cruising"] = cruising,
                ["thrust"] = thrust, ["coast"] = coast, ["turned"] = turned, ["navigated"] = navigated,
                ["navigationAccepted"] = navigationAccepted, ["navigationDistance"] = distance,
                ["arrivalDistance"] = SpaceGameplaySettings.Current.NavigationArrivalDistanceMeters,
                ["arrivalSpeed"] = SpaceGameplaySettings.Current.NavigationArrivalSpeedMetersPerSecond,
                ["shipTransition"] = shipTransition,
                ["landingAccepted"] = landingAccepted, ["landedAgain"] = landedAgain });
            yield return ReturnHome();
            homeChanged = false;
        }
        completed(new JObject { ["scope"] = "live-ship-flight", ["ships"] = results });
    }

    private IEnumerator CrossOwnedShip(ShipState ship, Action<JObject> completed)
    {
        ShipFlightState flight = session.GetFlight(ship.ShipId);
        SpaceSectorId originalSector = ship.Sector;
        SpaceVector2 originalPosition = new(ship.PositionX, ship.PositionY);
        SpaceVector2 originalVelocity = new(ship.VelocityX, ship.VelocityY);
        SpaceSectorId source = FindEmptySector(), destination = new(source.X + 1, source.Y);
        // 仅预置本用例刚生成的船体位置；越界与场景过渡仍由正式固定步完成。
        var start = new SpaceVector2(SpaceSectorCoordinates.HalfSizeMeters - 2048d, 0d);
        SetOwnedShipPose(ship, flight, source, start, default);
        try
        {
            yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(source),
                30d, "测试船起始星区场景未激活。");
            SetOwnedShipPose(ship, flight, source, start, new SpaceVector2(512d, 0d));
            JObject before = SessionSnapshot();
            yield return WaitUntil(() => Ready() && ship.Sector == destination &&
                    SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(destination),
                15d, "真实船体固定步未完成跨星区过渡。");
            JObject crossed = SessionSnapshot();
            yield return WaitUntil(() => !Contains(session.RequiredSectors, source), 20d, "船体离开过渡带后源星区未释放。");
            completed(new JObject { ["fixtureSetup"] = "owned-ship-preposition", ["shipId"] = ship.ShipId,
                ["source"] = Sector(source), ["destination"] = Sector(destination), ["before"] = before,
                ["crossed"] = crossed, ["settled"] = SessionSnapshot(), ["shipSector"] = Sector(ship.Sector),
                ["flightSector"] = Sector(flight.Motion.Sector), ["phase"] = flight.Phase.ToString() });
        }
        finally { SetOwnedShipPose(ship, flight, originalSector, originalPosition, originalVelocity); }
        yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(originalSector),
            30d, "测试船未恢复起飞星区。");
    }

    private void SetOwnedShipPose(ShipState ship, ShipFlightState flight, SpaceSectorId sector,
        SpaceVector2 position, SpaceVector2 velocity)
    {
        session.ClearControl(controlledConsole);
        session.GetUniverse(sector);
        ship.Sector = sector; ship.PositionX = position.X; ship.PositionY = position.Y;
        ship.VelocityX = velocity.X; ship.VelocityY = velocity.Y;
        flight.Motion.Sector = sector; flight.Motion.PositionMeters = position; flight.Motion.VelocityMetersPerSecond = velocity;
        session.RestorePassengerPose(CurrentPlayer());
    }

    private IEnumerator PrepareSite(FixedStructureDefinition template)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("地表区块管理器缺失。");
        ChunkGenerationProfileSnapshot profileData = manager.ActiveGenerationProfile;
        if (profileData == null) throw new InvalidOperationException("测试地表生成配置缺失。");
        int radius = template.SearchRadius + Math.Max(template.Width, template.Height) + template.Clearance;
        Vector2 center = WorldLocalPresentation.ToLogical((Vector2)CurrentPlayer().transform.position);
        RuntimeAddress first = manager.ResolveWorldAddress(center - Vector2.one * radius);
        var tasks = new List<Task<ChunkRuntime>>();
        int seed = DimensionManager.Instance.GetActiveGenerationSeed(SaveDataMgr.Instance.SaveData.Seed);
        for (int y = first.ChunkOrigin.Y; y <= center.y + radius; y += profileData.Height)
        for (int x = first.ChunkOrigin.X; x <= center.x + radius; x += profileData.Width)
        {
            RuntimeAddress address = manager.ResolveWorldAddress(new Vector2(x, y));
            leases.Add(manager.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
            tasks.Add(manager.RequestChunkDataAsync(address, seed, profileData, cancellation.Token));
        }
        yield return WaitUntil(() => tasks.TrueForAll(task => task.IsCompleted), 60d, "飞船测试场地加载超时。");
        foreach (Task<ChunkRuntime> task in tasks)
            if (task.Status != TaskStatus.RanToCompletion || task.Result?.DataStatus != ChunkDataStatus.Ready)
                throw new InvalidOperationException("飞船测试场地生成失败。", task.Exception);
        yield return Wait(.25d);
    }
    #endregion

    #region 迁移等待与真实状态采样
    private IEnumerator Teleport(SpaceMotionState motion)
    {
        bool done = false, succeeded = false;
        IsMigrationExpected = true;
        homeChanged = true;
        try
        {
            if (!session.TryTeleportPlayerToSpace(CurrentPlayer(), motion, value => { succeeded = value; done = true; }))
                throw new InvalidOperationException("正式太空传送入口拒绝测试入场。");
            yield return WaitUntil(() => done && Ready(), 120d, "太空入场迁移超时。");
            if (!succeeded || !session.IsSpaceView) throw new InvalidOperationException("太空入场事务失败。");
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SpaceSectorCoordinates.SceneKey(motion.Sector),
                30d, "正式太空入场没有激活目标星区场景。");
            ProtectCurrentPlayer();
        }
        finally { IsMigrationExpected = false; }
    }

    private IEnumerator ReturnHome()
    {
        if (!Ready()) yield return WaitUntil(Ready, 120d, "用例恢复前世界未就绪。");
        if (!string.IsNullOrEmpty(controlledConsole)) session.ClearControl(controlledConsole);
        if (SceneManager.GetActiveScene().name != home.WorldKey)
        {
            // 用例清理复用中立乘员迁移事务，不把清理传送计作降落验证。
            IsMigrationExpected = true;
            try
            {
                if (!session.TryBeginRespawnTransition(CurrentPlayer(), home, homePosition, () => { }))
                    throw new InvalidOperationException("正式清理迁移被拒绝。");
                yield return WaitUntil(() => Ready() && SceneManager.GetActiveScene().name == home.WorldKey,
                    120d, "无法恢复本用例起始测试世界。");
            }
            finally { IsMigrationExpected = false; }
        }
        else
        {
            session.ResetPassengerForRespawn(CurrentPlayer());
            Camera camera = Camera.main;
            Mod_PlayerTraits traits = CurrentPlayer().itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
            if (camera == null || traits == null || !traits.TryTeleportToScreenPosition(
                    camera.WorldToScreenPoint(WorldLocalPresentation.ProjectPosition(homePosition))))
                throw new InvalidOperationException("清理无法恢复测试玩家位置。");
        }
        homeChanged = false;
    }

    private IEnumerator Wait(double seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        yield return WaitUntil(() => Time.realtimeSinceStartupAsDouble >= until, seconds + 2d, "测试等待被中断。");
    }
    private IEnumerator WaitUntil(Func<bool> condition, double timeout, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition())
        {
            if (disposed || !Application.isPlaying) throw new OperationCanceledException(message);
            if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException(message);
            yield return null;
        }
    }

    private bool Ready() => GameManager.Instance?.IsGameplayReady == true &&
        DimensionManager.ExistingInstance?.IsTransitioning != true && ItemMgr.Instance?.User_Player != null &&
        ItemMgr.Instance.User_Player.ProfileName == profile;
    private Player CurrentPlayer() => ItemMgr.Instance?.User_Player ?? throw new InvalidOperationException("当前测试玩家缺失。");
    private void ProtectPlayer()
    {
        Mod_PlayerAdminController admin = CurrentPlayer().GetComponentInChildren<Mod_PlayerAdminController>(true);
        initialInvincibility = admin?.IsAdminInvincibilityEnabled == true;
        protectionChanged = true;
        ProtectCurrentPlayer();
    }
    private void ProtectCurrentPlayer()
    {
        if (!GameplayTestGm.TryExecute(CurrentPlayer(), "enable_invincibility", null, out string message))
            throw new InvalidOperationException(message);
    }

    private JObject SessionSnapshot()
    {
        Player player = CurrentPlayer();
        SpacePassengerState passenger = session.GetPassenger(player);
        SpaceSectorPosition position = session.GetPassengerPosition(passenger);
        var required = new JArray();
        foreach (SpaceSectorId sector in session.RequiredSectors) required.Add(Sector(sector));
        var scenes = new JArray();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && SpaceSectorCoordinates.TryParseSceneKey(scene.name, out SpaceSectorId sector))
                scenes.Add(Sector(sector));
        }
        return new JObject { ["version"] = session.State.Version, ["seconds"] = session.State.SimulationSeconds,
            ["sectorCount"] = session.State.Sectors.Count, ["scene"] = SceneManager.GetActiveScene().name,
            ["spaceView"] = session.IsSpaceView, ["playerProfile"] = player.ProfileName,
            ["playerInstance"] = player.GetInstanceID(), ["sector"] = Sector(position.Sector),
            ["position"] = Vector(position.LocalMeters), ["velocity"] = Vector(passenger.FreeMotion.VelocityMetersPerSecond),
            ["realmRevision"] = passenger.RealmRevision, ["required"] = required, ["loadedScenes"] = scenes,
            ["dimensionTransitioning"] = DimensionManager.Instance.IsTransitioning };
    }

    private JObject ShipSnapshot(ShipState ship)
    {
        ShipFlightState flight = session.GetFlight(ship.ShipId);
        decimal hydrogen = 0m, oxygen = 0m;
        var inventories = new HashSet<FluidInventory>();
        var nodes = new List<MachineEntity>();
        using (MachineWorld.UseScope("ship:" + ship.ShipId))
        {
            MachineWorld.CollectNodes(nodes);
            foreach (MachineEntity node in nodes)
            {
                if (node.Definition.Fluid?.Kind != "tank") continue;
                FluidInventory inventory = MachineWorld.GetFluidInventory(node);
                if (inventory == null || !inventories.Add(inventory)) continue;
                hydrogen += inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas);
                oxygen += inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas);
            }
        }
        double output = 0d;
        foreach (ShipPieceState piece in ship.Pieces) output += session.GetEngineOutput(piece.PieceId);
        return new JObject { ["id"] = ship.ShipId, ["phase"] = flight?.Phase.ToString(),
            ["sector"] = Sector(ship.Sector), ["position"] = Vector(new SpaceVector2(ship.PositionX, ship.PositionY)),
            ["velocity"] = Vector(new SpaceVector2(ship.VelocityX, ship.VelocityY)), ["angle"] = ship.AngleRadians,
            ["mass"] = ship.MassKg, ["pieceCount"] = ship.Pieces.Count, ["height"] = flight?.HeightMeters,
            ["hydrogen"] = (double)hydrogen, ["oxygen"] = (double)oxygen, ["engineOutput"] = output,
            ["spaceView"] = session.IsSpaceView, ["surfaceWorld"] = flight?.SurfaceWorldKey };
    }

    private static bool Contains(IReadOnlyCollection<SpaceSectorId> values, SpaceSectorId sector)
    { foreach (SpaceSectorId value in values) if (value == sector) return true; return false; }
    private static JObject Sector(SpaceSectorId value) => new() {
        ["x"] = value.X.ToString(CultureInfo.InvariantCulture), ["y"] = value.Y.ToString(CultureInfo.InvariantCulture) };
    private static JObject Vector(SpaceVector2 value) => new() { ["x"] = value.X, ["y"] = value.Y };
    private static JObject Vector(Vector2 value) => new() { ["x"] = value.x, ["y"] = value.y };
    #endregion

    #region 异常收尾
    private static IEnumerator Advance(IEnumerator routine, Action<Exception> failed)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        try
        {
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                Exception failure = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                    else (stack.Pop() as IDisposable)?.Dispose();
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { failed(failure); yield break; }
                if (!moved) continue;
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
        }
        finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsMigrationExpected = false;
        cancellation.Cancel();
        cancellation.Dispose();
        if (!string.IsNullOrEmpty(controlledConsole)) session?.ClearControl(controlledConsole);
        var cleanupErrors = new List<Exception>();
        for (int index = contactFixtures.Count - 1; index >= 0; index--)
        {
            try
            {
                var fixture = contactFixtures[index];
                if (session?.State != null && !session.RollbackPiece(fixture.Ship, fixture.Piece) &&
                    session.GetShip(fixture.Ship.ShipId) != null)
                    throw new InvalidOperationException("船体接触夹具回收失败：" + fixture.Ship.ShipId);
            }
            catch (Exception exception) { cleanupErrors.Add(exception); }
        }
        contactFixtures.Clear();
        foreach (ChunkLease lease in leases) lease.Dispose();
        leases.Clear();
        if (protectionChanged && ItemMgr.Instance?.User_Player != null)
        {
            Mod_PlayerAdminController admin = CurrentPlayer().GetComponentInChildren<Mod_PlayerAdminController>(true);
            if (admin?.IsAdministrator == true) admin.TrySetAdminInvincibilityEnabled(initialInvincibility);
        }
        if (cleanupErrors.Count > 0) throw new AggregateException("太空测试夹具清理失败。", cleanupErrors);
    }
    #endregion
}
