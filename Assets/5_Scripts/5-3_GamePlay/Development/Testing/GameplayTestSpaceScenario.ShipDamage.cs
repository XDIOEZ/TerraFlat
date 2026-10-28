using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Spaceflight;
using FlatWorld.Structures;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class GameplayTestSpaceScenario
{
    #region 自有长船夹具与真实分裂流程

    private readonly HashSet<string> damageFixtureIds = new(StringComparer.Ordinal);
    private readonly HashSet<int> damageFixtureDrops = new();
    private string damageCargoId;

    private IEnumerator DamageProductionLongShip(Action<JObject> completed)
    {
        IReadOnlyList<FixedStructureDefinition> templates = null;
        Exception loadError = null;
        yield return FixedStructureCatalog.LoadAsync(value => templates = value, error => loadError = error);
        if (loadError != null) throw loadError;
        var parts = new Dictionary<ShipPieceKind, FixedStructureMember>();
        FixedStructureMember cargoMember = null;
        foreach (FixedStructureDefinition template in templates)
        foreach (FixedStructureMember member in template.Members)
        {
            ItemData candidate = GameRes.ExistingInstance.CreateItemData(member.ItemId);
            ShipPartConfiguration config = SpaceSession.ResolvePartConfiguration(candidate);
            if (config == null || config.FootprintWidth != 1 || config.FootprintHeight != 1) continue;
            if (!parts.ContainsKey(config.Kind)) parts.Add(config.Kind, member);
            if (member.Resources?.Inventories?.Count > 0 &&
                candidate.ModuleDataDic?.Values != null)
                foreach (ModuleData module in candidate.ModuleDataDic.Values)
                    if (module.ID == Mod_Cultivator.ModuleId) cargoMember ??= member;
        }
        if (!parts.ContainsKey(ShipPieceKind.Floor) || !parts.ContainsKey(ShipPieceKind.Console) ||
            !parts.ContainsKey(ShipPieceKind.Engine) || !parts.ContainsKey(ShipPieceKind.Navigation) || cargoMember == null)
            throw new InvalidOperationException("正式模板缺少一格地板、控制台、引擎、导航器或带库存培育器。");

        ProtectPlayer();
        SpaceSectorId sector = FindEmptySector();
        yield return Teleport(new SpaceMotionState { Sector = sector, PositionMeters = new SpaceVector2(4.5d, 3d), RadiusMeters = .3d });
        var floors = new List<ShipPieceState>();
        ShipState original = null;
        for (int x = 0; x < 9; x++)
        {
            ShipPieceState floor = AddDamageFixture(parts[ShipPieceKind.Floor], sector, x, 0, out ShipState owner);
            original ??= owner;
            if (!ReferenceEquals(original, owner)) throw new InvalidOperationException("正式放置事务没有把长船地板连成一体。");
            floors.Add(floor);
        }
        ShipPieceState console = AddDamageFixture(parts[ShipPieceKind.Console], sector, 0, 0, out _);
        ShipPieceState engine = AddDamageFixture(parts[ShipPieceKind.Engine], sector, 1, 0, out _);
        ShipPieceState navigation = AddDamageFixture(parts[ShipPieceKind.Navigation], sector, 2, 0, out _);
        ShipPieceState cargo = AddDamageFixture(cargoMember, sector, 7, 0, out _);
        damageCargoId = cargo.PieceId;
        // 锚点故意留在船尾，检查原身份成为残骸时新船首的飞行与导航不会被清除。
        original.IdentityAnchorPieceId = floors[8].PieceId;
        session.GetFlight(original.ShipId).Ignited = true;
        var navigationState = new ShipNavigationState { ShipId = original.ShipId,
            NavigationPieceId = navigation.PieceId, ConsolePieceId = console.PieceId,
            TargetSector = sector, Active = false, Sequence = ++session.State.NavigationSequence };
        session.State.Navigation.Add(navigationState);
        bool headDamage = DamageOwnedPiece(floors[2], 17f);
        // 尾部伤者选最终仍存活的锚点地板，不能把后续主动摧毁的小块当作保存基准。
        bool tailDamage = DamageOwnedPiece(floors[8], 11f);
        bool cargoDamage = DamageOwnedPiece(cargo, 7f);
        yield return Wait(.25d);
        // 培育器会先把种子放进种植槽，基线必须捕获当前真实库存而不是最初放置的旧快照。
        using (MachineWorld.UseScope("ship:" + original.ShipId))
            session.CaptureMachinePieceSnapshot(cargo, MachineWorld.GetById(int.Parse(cargo.PieceId, CultureInfo.InvariantCulture)));
        JArray before = DamageFragmentReadings(CollectDamageFragments());
        double beforeTime = session.State.SimulationSeconds;
        bool waistHit = DamageOwnedPiece(floors[3], 1000f);
        yield return WaitUntil(() => CollectDamageFragments().Count == 2, 15d, "腰部损伤没有通过真实固定步拆成两段。");
        yield return Wait(.25d);
        JArray afterWaist = DamageFragmentReadings(CollectDamageFragments());
        bool coldCargoDamage = DamageOwnedPiece(cargo, 3f);
        bool tailHit = DamageOwnedPiece(floors[6], 1000f);
        yield return WaitUntil(() => CollectDamageFragments().Count == 3, 15d, "真实冷残骸没有继续拆成两个碎块。");
        yield return Wait(.25d);
        JArray afterTail = DamageFragmentReadings(CollectDamageFragments());
        // 继续打掉货物支撑和小块的最后地板，检查真实冷移交与空整体清理。
        bool cargoFloorHit = DamageOwnedPiece(floors[7], 1000f);
        yield return WaitUntil(() => CollectDamageFragments().Count == 4, 15d, "失去地板的冷货物没有成为独立残骸。");
        yield return Wait(.25d);
        JArray afterUnsupported = DamageFragmentReadings(CollectDamageFragments());
        session.FindPiece(floors[4].PieceId, out ShipState emptyCandidate, out _);
        string emptiedShipId = emptyCandidate?.ShipId;
        int emptiedWreckGuid = emptyCandidate?.WreckGuid ?? 0;
        bool smallFloorHit = DamageOwnedPiece(floors[4], 1000f);
        yield return WaitUntil(() => !session.FindPiece(floors[4].PieceId, out _, out _), 15d, "第一块冷地板摧毁后仍可查询。");
        yield return Wait(.25d);
        bool lastFloorHit = DamageOwnedPiece(floors[5], 1000f);
        yield return WaitUntil(() => session.GetShip(emptiedShipId) == null, 15d, "最后一块地板摧毁后空残骸没有移除。");
        yield return Wait(.25d);
        JArray afterEmpty = DamageFragmentReadings(CollectDamageFragments());
        var destroyedReadings = new JArray();
        foreach (int index in new[] { 3, 6, 7, 4, 5 })
            destroyedReadings.Add(new JObject { ["id"] = floors[index].PieceId,
                ["indexFound"] = session.FindPiece(floors[index].PieceId, out _, out _) });
        int removedWreckViews = 0, removedPhysicsBodies = 0;
        foreach (ShipWreckView view in Resources.FindObjectsOfTypeAll<ShipWreckView>())
            if (view != null && (view.Composition?.ShipId == emptiedShipId || view.itemData?.Guid == emptiedWreckGuid)) removedWreckViews++;
        foreach (Rigidbody2D body in Resources.FindObjectsOfTypeAll<Rigidbody2D>())
            if (body != null && body.simulated && body.gameObject.name == "ship:" + emptiedShipId) removedPhysicsBodies++;
        SpaceSession.Capture(SaveDataMgr.Instance.SaveData);
        SpaceSessionState restored = SaveDataMgr.DeserializeSpaceState(SaveDataMgr.Instance.SaveData.SpaceStateJson);
        var saved = new List<ShipState>();
        foreach (ShipState ship in restored.Ships)
            if (ship.Pieces.Exists(piece => damageFixtureIds.Contains(piece.PieceId))) saved.Add(ship);
        completed(new JObject
        {
            ["scope"] = "live-ship-local-damage", ["fixtureSetup"] = "production-piece-transactions-tail-anchor",
            ["originalId"] = original.ShipId, ["consoleId"] = console.PieceId, ["engineId"] = engine.PieceId,
            ["navigationId"] = navigation.PieceId, ["headWoundedId"] = floors[2].PieceId,
            ["tailWoundedId"] = floors[8].PieceId, ["cargoId"] = cargo.PieceId,
            ["headDamageAccepted"] = headDamage, ["tailDamageAccepted"] = tailDamage, ["cargoDamageAccepted"] = cargoDamage,
            ["coldCargoDamageAccepted"] = coldCargoDamage,
            ["waistHitAccepted"] = waistHit, ["tailHitAccepted"] = tailHit,
            ["cargoFloorHitAccepted"] = cargoFloorHit, ["smallFloorHitAccepted"] = smallFloorHit,
            ["lastFloorHitAccepted"] = lastFloorHit,
            ["before"] = before, ["afterWaist"] = afterWaist, ["afterTail"] = afterTail,
            ["afterUnsupported"] = afterUnsupported, ["afterEmpty"] = afterEmpty,
            ["destroyedPieces"] = destroyedReadings,
            ["removedFragment"] = new JObject { ["id"] = emptiedShipId, ["wreckGuid"] = emptiedWreckGuid,
                ["shipPresent"] = session.GetShip(emptiedShipId) != null,
                ["flightPresent"] = session.GetFlight(emptiedShipId) != null,
                ["flightCount"] = CountDamageFlights(emptiedShipId), ["wreckViews"] = removedWreckViews,
                ["physicsBodies"] = removedPhysicsBodies },
            ["saved"] = DamageFragmentReadings(saved, includeViews: false),
            ["beforeTime"] = beforeTime, ["afterTime"] = session.State.SimulationSeconds,
            ["navigationShipId"] = navigationState.ShipId,
            ["navigationPresent"] = session.State.Navigation.Contains(navigationState),
            ["limitations"] = "直接使用已结算伤害测试正式分裂、冷库存和真实组件，不包含武器投射物、防御投送、实际供电供油逃跑或多人同步。"
        });
    }

    private ShipPieceState AddDamageFixture(FixedStructureMember member, SpaceSectorId sector,
        int x, int y, out ShipState ship)
    {
        ItemData data = GameRes.ExistingInstance.CreateItemData(member.ItemId);
        data.Stack.Amount = 1f;
        Mod_Building.SetInstalledDataState(data);
        FixedStructureResourceInitializer.Prepare(data, member.Resources);
        ShipPieceState piece = session.AddPiece(data, session.ProjectPosition(
            new SpaceSectorPosition(sector, new SpaceVector2(x + .5d, y + .5d))), 0, out ship);
        // 自有夹具只隔离温压的被动伤害，局部命中和结构分裂仍走正式规则。
        piece.TemperatureDamagePerCelsiusSecond = piece.PressureDamagePerKPaSecond = 0d;
        damageFixtureIds.Add(piece.PieceId);
        if (MachineCatalog.Get(piece.ItemId) != null)
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
            {
                MachineEntity node = MachineWorld.GetById(data.Guid);
                FixedStructureResourceInitializer.ApplyInventories(node, member.Resources);
                session.CaptureMachinePieceSnapshot(piece, node);
            }
        session.CommitPiece(ship, piece);
        return piece;
    }

    private bool DamageOwnedPiece(ShipPieceState piece, float damage)
    {
        var before = new HashSet<string>();
        foreach (SpaceLooseItemState item in session.State.LooseItems) before.Add(item.Id);
        bool accepted = session.ApplyPieceDamage(int.Parse(piece.PieceId, CultureInfo.InvariantCulture), damage, "隔离长船局部伤害样本");
        // 只登记本次同步伤害入口新生成的掉落，清理时不会删除原有散落物。
        foreach (SpaceLooseItemState item in session.State.LooseItems)
            if (!before.Contains(item.Id)) damageFixtureDrops.Add(SpaceSession.Decode(item.Snapshot).Guid);
        return accepted;
    }

    private List<ShipState> CollectDamageFragments()
    {
        var result = new List<ShipState>();
        foreach (ShipState ship in session.State.Ships)
            if (ship.Pieces.Exists(piece => piece.IsAlive && damageFixtureIds.Contains(piece.PieceId))) result.Add(ship);
        return result;
    }

    #endregion

    #region 权威碎块与实际组件采样

    private JArray DamageFragmentReadings(IEnumerable<ShipState> fragments, bool includeViews = true)
    {
        var result = new JArray();
        Rigidbody2D[] bodies = includeViews ? Resources.FindObjectsOfTypeAll<Rigidbody2D>() : Array.Empty<Rigidbody2D>();
        ShipWreckView[] wreckViews = includeViews ? Resources.FindObjectsOfTypeAll<ShipWreckView>() : Array.Empty<ShipWreckView>();
        foreach (ShipState ship in fragments)
        {
            var pieces = new JArray();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                var reading = new JObject { ["id"] = piece.PieceId, ["kind"] = piece.Kind.ToString(), ["health"] = piece.Health };
                if (includeViews)
                {
                    bool indexFound = session.FindPiece(piece.PieceId, out ShipState indexedShip, out ShipPieceState indexedPiece);
                    reading["indexFound"] = indexFound;
                    reading["indexOwner"] = indexedShip?.ShipId ?? string.Empty;
                    reading["sameIndexedPiece"] = ReferenceEquals(piece, indexedPiece);
                }
                if (piece.PieceId == damageCargoId)
                {
                    ItemData cold = string.IsNullOrEmpty(piece.SnapshotJson) ? null : SpaceSession.Decode(piece.SnapshotJson);
                    CultivatorState inventory = cold == null ? null : MachineModuleState.Read<CultivatorState>(cold, Mod_Cultivator.ModuleId);
                    double seedAmount = 0d, fertilizerAmount = 0d;
                    int plantedPlots = 0;
                    if (inventory?.Seeds != null)
                        foreach (ItemSlot slot in inventory.Seeds.CreateColdData().itemSlots) seedAmount += slot?.itemData?.Stack?.Amount ?? 0f;
                    if (inventory?.Fertilizer != null)
                        foreach (ItemSlot slot in inventory.Fertilizer.CreateColdData().itemSlots) fertilizerAmount += slot?.itemData?.Stack?.Amount ?? 0f;
                    if (inventory?.Plots != null)
                        foreach (CultivatorPlotState plot in inventory.Plots) if (!string.IsNullOrEmpty(plot.CropId)) plantedPlots++;
                    reading["snapshotPresent"] = cold != null;
                    reading["decodedGuid"] = cold?.Guid ?? 0;
                    reading["decodedId"] = cold?.IDName ?? string.Empty;
                    reading["seeds"] = seedAmount; reading["fertilizer"] = fertilizerAmount;
                    reading["plantedPlots"] = plantedPlots;
                    reading["snapshotHealth"] = cold == null ? -1f : MachineWorld.ReadMachineState(cold).Hp;
                    if (includeViews)
                        using (MachineWorld.UseScope("ship:" + ship.ShipId))
                        {
                            reading["machineActive"] = MachineWorld.GetById(int.Parse(piece.PieceId, CultureInfo.InvariantCulture)) != null;
                            reading["deviceActive"] = session.GetDevice(piece.PieceId) != null;
                        }
                }
                pieces.Add(reading);
            }
            ShipFlightState flight = includeViews ? session.GetFlight(ship.ShipId) : null;
            GameObject viewRoot = null;
            int wreckCount = 0, shellGuid = 0;
            foreach (ShipWreckView view in wreckViews)
                if (view != null && view.gameObject.activeInHierarchy && view.Composition?.ShipId == ship.ShipId)
                { wreckCount++; viewRoot = view.gameObject; shellGuid = view.itemData?.Guid ?? 0; }
            if (!ship.IsWreck && includeViews) viewRoot = GameObject.Find("ShipView_" + ship.ShipId);
            int physicsBodies = 0, physicsColliders = 0;
            ShipAssemblyState assembly = includeViews ? session.GetConnectedAssembly(ship.ShipId) : null;
            if (assembly != null)
                foreach (Rigidbody2D body in bodies)
                    if (body != null && body.simulated && body.gameObject.name == "ship:" + assembly.AssemblyId)
                    { physicsBodies++; physicsColliders += body.GetComponents<CustomCollider2D>().Length; }
            var fragmentSnapshot = new JObject
            {
                ["id"] = ship.ShipId, ["wreck"] = ship.IsWreck,
                ["retainsInteriorWorld"] = ship.RetainsInteriorWorld,
                ["retainsWorldWithoutStructure"] = ship.RetainsWorldWithoutStructure,
                ["console"] = ShipStructureService.HasOperationalConsole(ship),
                ["engine"] = ShipStructureService.HasOperationalEngine(ship), ["pieces"] = pieces,
                ["phase"] = flight?.Phase.ToString() ?? string.Empty, ["ignited"] = flight?.Ignited == true,
                ["flightCount"] = includeViews ? CountDamageFlights(ship.ShipId) : 0,
                ["wreckViewCount"] = wreckCount, ["wreckGuid"] = ship.WreckGuid, ["shellGuid"] = shellGuid,
                ["presentationBodies"] = viewRoot == null ? 0 : viewRoot.GetComponentsInChildren<Rigidbody2D>(true).Length,
                ["presentationColliders"] = viewRoot == null ? 0 : viewRoot.GetComponentsInChildren<CustomCollider2D>(true).Length,
                ["presentationItems"] = viewRoot == null ? 0 : viewRoot.GetComponentsInChildren<Item>(true).Length,
                ["physicsBodies"] = physicsBodies, ["physicsColliders"] = physicsColliders
            };
            if (includeViews)
            {
                bool hasRuntime = session.TryGetShipInteriorWorld(ship.ShipId, out ShipInteriorWorldRuntime interior);
                bool registered = GameplayWorldRegistry.TryGet(MachineWorld.GetShipScopeKey(ship.ShipId), out GameplayWorldRuntime world);
                fragmentSnapshot["hasShipInteriorRuntime"] = hasRuntime;
                fragmentSnapshot["hasRegisteredInteriorWorld"] = registered;
                fragmentSnapshot["interiorWorldUsesLocalCoordinates"] = registered && world.UsesLocalCoordinates;
                fragmentSnapshot["interiorWorldMatchesRuntime"] = registered && hasRuntime &&
                    world.Scene == interior.Scene && ReferenceEquals(world.Chunks, interior.Chunks);
            }
            result.Add(fragmentSnapshot);
        }
        return result;
    }

    private int CountDamageFlights(string id)
    {
        int count = 0;
        foreach (ShipFlightState flight in session.State.Flights) if (flight.ShipId == id) count++;
        return count;
    }

    private void CleanupDamageFixtures()
    {
        if (session?.State == null) return;
        foreach (ShipState ship in session.State.Ships.ToArray())
            foreach (ShipPieceState piece in ship.Pieces.ToArray())
                if (damageFixtureIds.Contains(piece.PieceId)) session.RollbackPiece(ship, piece);
        foreach (int guid in damageFixtureDrops) session.RemoveNetworkLooseItem(guid);
        damageFixtureIds.Clear(); damageFixtureDrops.Clear(); damageCargoId = null;
    }

    #endregion
}
