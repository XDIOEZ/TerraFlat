using System;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using FlatWorld.Structures;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public static class ShipFixedStructureBuilder
    {
        #region 固定船型生成事务
        public const string TemplateMarker = "flatworld.fixed-structure";

        private sealed class PreparedMember
        {
            public FixedStructureMember Member;
            public ItemData Data;
            public bool IsFloor;
        }

        private readonly struct AddedPiece
        {
            public readonly ShipState Ship;
            public readonly ShipPieceState Piece;
            public AddedPiece(ShipState ship, ShipPieceState piece) { Ship = ship; Piece = piece; }
        }

        public static bool TryBuild(Player player, FixedStructureDefinition template, out ShipState ship, out string reason)
        {
            ship = null;
            reason = null;
            SpaceSession session = SpaceSession.Current;
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            if (GameNetwork.IsOnline || GameManager.Instance?.IsGameplayReady != true || player == null ||
                session?.State == null || session.IsSpaceView || chunks == null || !chunks.IsAuthoritativeSimulation ||
                !chunks.IsWorldModelRuntimeActive || chunks.ActiveGenerationProfile?.Settings.Mode != ChunkGenerationMode.Surface)
            { reason = "请在单机地表世界就绪后生成飞船。"; return false; }

            var added = new List<AddedPiece>();
            var originalShips = new HashSet<string>();
            foreach (ShipState existing in session.State.Ships) originalShips.Add(existing.ShipId);
            try
            {
                if (template == null || !string.Equals(template.Kind, "ship", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("当前模板不是飞船结构。" );
                List<PreparedMember> members = PrepareMembers(template);
                if (template.CabinGas != null)
                    FixedStructureResourceInitializer.CreateFluidState(template.CabinGas, null, 1d, .001d, double.MaxValue);
                Vector2Int origin;
                using (MachineWorld.UseScope(SceneManager.GetActiveScene().name))
                    if (!TryFindSite(player, template, chunks, session, out origin))
                    { reason = "附近没有已加载、干燥且无障碍的完整空地，请移到更开阔的位置。"; return false; }

                foreach (PreparedMember entry in members)
                {
                    Vector2 position = WorldLocalPresentation.ProjectPosition(new Vector2(origin.x + entry.Member.X + .5f,
                        origin.y + entry.Member.Y + .5f));
                    ShipPieceState piece = session.AddPiece(entry.Data, position, entry.Member.QuarterTurns, out ShipState owner);
                    added.Add(new AddedPiece(owner, piece));
                    if (originalShips.Contains(owner.ShipId)) throw new InvalidOperationException("生成位置与已有飞船相邻，请重新选址。" );
                    ship ??= owner;
                    if (!ReferenceEquals(ship, owner)) throw new InvalidOperationException("模板地板必须连接成一艘完整飞船。" );
                    if (MachineCatalog.Get(entry.Data.IDName) != null)
                    {
                        using (MachineWorld.UseScope("ship:" + ship.ShipId))
                        {
                            MachineEntity node = MachineWorld.GetById(entry.Data.Guid);
                            if (node == null) throw new InvalidOperationException("生成的船上机器没有登记。" );
                            FixedStructureResourceInitializer.ApplyInventories(node, entry.Member.Resources);
                            CaptureMachinePiece(session, piece, node);
                        }
                    }
                }
                if (ship == null) throw new InvalidOperationException("模板没有可生成的船体。" );
                session.CommitPiece(ship, added[added.Count - 1].Piece);
                FillCabins(ship, template.CabinGas);
                ShipDockingService.SynchronizePorts(ship);
                reason = "已生成“" + template.DisplayName + "”，可直接进入船舱测试。";
                return true;
            }
            catch (Exception error)
            {
                // 候选按相反顺序撤销，失败时不拆除现场已有的部件。
                var failures = new List<string>();
                for (int i = added.Count - 1; i >= 0; i--)
                    try { if (!session.RollbackPiece(added[i].Ship, added[i].Piece)) failures.Add(added[i].Piece.PieceId); }
                    catch (Exception rollback) { failures.Add(rollback.Message); }
                ship = null;
                reason = "生成失败：" + error.Message;
                if (failures.Count > 0) reason += "；部分候选撤销失败：" + string.Join("；", failures);
                return false;
            }
        }

        private static List<PreparedMember> PrepareMembers(FixedStructureDefinition template)
        {
            var members = new List<PreparedMember>();
            if (template.Members == null || template.Members.Count == 0) throw new InvalidOperationException("模板没有部件。" );
            foreach (FixedStructureMember member in template.Members)
            {
                ItemData data = GameRes.ExistingInstance.CreateItemData(member.ItemId);
                if (data == null) throw new InvalidOperationException("找不到模板物品：" + member.ItemId);
                data.Stack.Amount = 1f;
                Mod_Building.SetInstalledDataState(data);
                FixedStructureResourceInitializer.Prepare(data, member.Resources);
                JObject special = ItemSpecialDataJsonStore.ReadRoot(data.ItemSpecialData);
                special[TemplateMarker] = new JObject { ["templateId"] = template.Id, ["memberId"] = member.MemberId };
                data.ItemSpecialData = special.ToString(Formatting.None);
                members.Add(new PreparedMember { Member = member, Data = data,
                    IsFloor = Mod_ShipPart.Read(data)?.Kind == ShipPieceKind.Floor });
            }
            if (!members.Exists(value => value.IsFloor)) throw new InvalidOperationException("飞船模板至少需要一个承载地板。" );
            members.Sort((left, right) =>
            {
                int floor = right.IsFloor.CompareTo(left.IsFloor);
                if (floor != 0) return floor;
                int row = left.Member.Y.CompareTo(right.Member.Y);
                return row != 0 ? row : left.Member.X.CompareTo(right.Member.X);
            });
            var ordered = new List<PreparedMember>();
            var connected = new HashSet<ShipCell>();
            var remaining = members.FindAll(value => value.IsFloor);
            while (remaining.Count > 0)
            {
                int found = -1;
                for (int i = 0; i < remaining.Count; i++)
                {
                    ShipPieceState floor = MemberFootprint(remaining[i], template);
                    bool joins = connected.Count == 0;
                    foreach (ShipCell cell in ShipGeometry.Footprint(floor))
                        foreach (ShipCell adjacent in ShipGeometry.Neighbors(cell))
                            if (connected.Contains(adjacent)) joins = true;
                    if (joins) { found = i; break; }
                }
                if (found < 0) throw new InvalidOperationException("模板地板必须四邻连通。" );
                PreparedMember next = remaining[found];
                foreach (ShipCell cell in ShipGeometry.Footprint(MemberFootprint(next, template))) connected.Add(cell);
                ordered.Add(next); remaining.RemoveAt(found);
            }
            foreach (PreparedMember member in members)
            {
                ShipPieceState proposal = MemberFootprint(member, template);
                if (!member.IsFloor)
                    foreach (ShipCell cell in ShipGeometry.Footprint(proposal))
                        if (!connected.Contains(cell)) throw new InvalidOperationException("模板设备缺少地板支撑：" + member.Member.MemberId);
            }
            ordered.AddRange(members.FindAll(value => !value.IsFloor));
            return ordered;
        }

        private static ShipPieceState MemberFootprint(PreparedMember member, FixedStructureDefinition template)
        {
            ShipPartConfiguration configuration = Mod_ShipPart.Read(member.Data);
            Mod_Building.TryReadBuildingData(member.Data, out _, out Mod_Building.Building_Data building);
            var piece = new ShipPieceState { CellX = member.Member.X, CellY = member.Member.Y,
                Width = Math.Max(1, Math.Max(configuration?.FootprintWidth ?? 1, building?.FootprintWidth ?? 1)),
                Height = Math.Max(1, Math.Max(configuration?.FootprintHeight ?? 1, building?.FootprintHeight ?? 1)),
                QuarterTurns = member.Member.QuarterTurns };
            foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                if (cell.X < 0 || cell.Y < 0 || cell.X >= template.Width || cell.Y >= template.Height)
                    throw new InvalidOperationException("模板部件实际占地超出结构尺寸：" + member.Member.MemberId);
            return piece;
        }

        private static void CaptureMachinePiece(SpaceSession session, ShipPieceState piece, MachineEntity node)
        {
            session.CaptureMachinePieceSnapshot(piece, node);
        }

        private static void FillCabins(ShipState ship, FixedStructureGas gas)
        {
            if (gas == null) return;
            if (ship.Compartments.Count == 0) throw new InvalidOperationException("模板配置了舱气，但船体没有密闭舱。" );
            foreach (ShipCompartmentState room in ship.Compartments)
                room.Gas = FixedStructureResourceInitializer.CreateFluidState(gas, null, room.VolumeLiters, .001d, double.MaxValue);
        }
        #endregion

        #region 地表空地选址
        private static bool TryFindSite(Player player, FixedStructureDefinition template, ChunkMgr chunks,
            SpaceSession session, out Vector2Int origin)
        {
            Vector2 logical = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            Vector2Int center = new(Mathf.FloorToInt(logical.x), Mathf.FloorToInt(logical.y));
            int radius = template.SearchRadius;
            int clearance = Math.Max(1, template.Clearance);
            var colliders = new Collider2D[128];
            var bounds = new List<Bounds>();
            var filter = new ContactFilter2D();
            filter.NoFilter();
            for (int ring = 0; ring <= radius; ring++)
            for (int y = -ring; y <= ring; y++)
            for (int x = -ring; x <= ring; x++)
            {
                if (Mathf.Abs(x) != ring && Mathf.Abs(y) != ring) continue;
                Vector2Int candidate = center + new Vector2Int(x - template.Width / 2, y - template.Height / 2);
                var area = new BoundsInt(candidate.x - clearance, candidate.y - clearance, 0,
                    template.Width + clearance * 2, template.Height + clearance * 2, 1);
                if (logical.x >= area.xMin && logical.x < area.xMax && logical.y >= area.yMin && logical.y < area.yMax) continue;
                if (!IsTerrainClear(area, chunks, session)) continue;
                NaturalEntityEcsService.CollectBlockingBounds(area, bounds);
                bool blocked = false;
                Vector2 logicalCenter = new(area.xMin + area.size.x * .5f, area.yMin + area.size.y * .5f);
                var footprint = new Bounds(logicalCenter, new Vector3(area.size.x - .02f, area.size.y - .02f, 2f));
                if (AiRuntimeBackendService.Ecology?.HasLivingActorInBounds(footprint) == true) continue;
                foreach (Bounds obstacle in bounds)
                {
                    Vector2 nearest = WorldLocalPresentation.ProjectPosition((Vector2)obstacle.center, logicalCenter);
                    var canonical = new Bounds(new Vector3(nearest.x, nearest.y, 0f), new Vector3(obstacle.size.x, obstacle.size.y, 2f));
                    if (footprint.Intersects(canonical)) { blocked = true; break; }
                }
                if (blocked) continue;
                int count = Physics2D.OverlapBox(WorldLocalPresentation.ProjectPosition(logicalCenter),
                    new Vector2(area.size.x - .02f, area.size.y - .02f), 0f, filter, colliders);
                if (count == colliders.Length) continue;
                for (int i = 0; i < count; i++)
                {
                    Collider2D collider = colliders[i];
                    if (collider == null) continue;
                    if (!collider.isTrigger) { blocked = true; break; }
                    Item item = collider.GetComponentInParent<Item>();
                    if (item == null) continue;
                    Vector2 point = WorldLocalPresentation.ToLogical((Vector2)item.transform.position);
                    if (point.x >= area.xMin && point.x < area.xMax && point.y >= area.yMin && point.y < area.yMax)
                    { blocked = true; break; }
                }
                if (blocked) continue;
                origin = candidate;
                return true;
            }
            origin = default;
            return false;
        }

        private static bool IsTerrainClear(BoundsInt area, ChunkMgr chunks, SpaceSession session)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                Vector2Int cell = new(x, y);
                if (WorldTopologyRuntime.NormalizeCell(cell) != cell ||
                    !chunks.TryGetRuntimeTerrainTile(new Vector2(x + .5f, y + .5f), out RuntimeTerrainTileSample sample) ||
                    sample.WorldCell != cell || sample.TopTileId == 0 || (sample.Cell.Flags & TerrainCellFlags.Walkable) == 0 ||
                    (sample.Cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) != 0 ||
                    sample.Cell.NavigationCost > 1000 || sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y) > 0f ||
                    TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0 ||
                    BuildingOccupancyRegistry.IsOccupied(cell) ||
                    session.TryGetSupport(WorldLocalPresentation.ProjectPosition(new Vector2(x + .5f, y + .5f)), out _, out _, out _)) return false;
                for (int layer = 0; layer <= 5; layer++) if (MachineWorld.IsOccupied(cell, layer)) return false;
            }
            return true;
        }
        #endregion

        #region 模板资源补充
        public static bool TryRefill(Player player, IReadOnlyList<FixedStructureDefinition> templates, out string reason)
        {
            reason = null;
            SpaceSession session = SpaceSession.Current;
            if (GameNetwork.IsOnline || GameManager.Instance?.IsGameplayReady != true || player == null || session?.State == null)
            { reason = "请在单机世界就绪后补充资源。"; return false; }
            ShipState nearest = null;
            double distance = 24d * 24d;
            foreach (ShipState candidate in session.State.Ships)
            {
                if (!session.IsVisible(candidate)) continue;
                bool generated = false;
                foreach (ShipPieceState piece in candidate.Pieces)
                    if (piece.IsAlive && ReadMarker(session.GetPieceData(piece)) != null) { generated = true; break; }
                if (!generated) continue;
                foreach (ShipPieceState piece in candidate.Pieces)
                {
                    if (!piece.IsAlive || piece.Kind != ShipPieceKind.Floor) continue;
                    Vector2 point = session.DisplayPoint(candidate, (piece.CellX + .5d) * candidate.CellSizeMeters,
                        (piece.CellY + .5d) * candidate.CellSizeMeters);
                    double sqr = (point - (Vector2)player.transform.position).sqrMagnitude;
                    if (sqr <= distance) { distance = sqr; nearest = candidate; }
                }
            }
            if (nearest == null) { reason = "24 米内没有由固定模板生成的飞船。"; return false; }
            try
            {
                var entries = new List<(ShipPieceState piece, MachineEntity node, FixedStructureMember member)>();
                using (MachineWorld.UseScope("ship:" + nearest.ShipId))
                {
                    var resources = new Dictionary<int, FixedStructureResources>();
                    foreach (ShipPieceState piece in nearest.Pieces)
                    {
                        if (!piece.IsAlive) continue;
                        JObject marker = ReadMarker(session.GetPieceData(piece));
                        if (marker == null) continue;
                        FixedStructureDefinition template = null;
                        foreach (FixedStructureDefinition value in templates)
                            if (value.Id == (string)marker["templateId"]) { template = value; break; }
                        if (template == null) throw new InvalidOperationException("缺少这艘船的固定结构模板。" );
                        FixedStructureMember member = template.Members.Find(value => value.MemberId == (string)marker["memberId"]);
                        if (member == null || member.ItemId != piece.ItemId) throw new InvalidOperationException("船体部件与当前模板不匹配。" );
                        ItemData prepared = GameRes.ExistingInstance.CreateItemData(member.ItemId);
                        FixedStructureResourceInitializer.Prepare(prepared, member.Resources);
                        MachineEntity node = MachineWorld.GetById(session.GetPieceData(piece).Guid);
                        if (node != null && member.Resources != null)
                        { entries.Add((piece, node, member)); resources.Add(node.Id, member.Resources); }
                    }
                    var groups = new List<MachineWorld.FluidTankGroup>();
                    MachineWorld.CollectFluidTankGroups(groups);
                    FixedStructureResourceInitializer.ValidateSharedResources(groups, resources);
                    var filled = new HashSet<FluidInventory>();
                    foreach (var entry in entries)
                    {
                        FixedStructureResourceInitializer.Refill(entry.node, entry.member.Resources, filled);
                        CaptureMachinePiece(session, entry.piece, entry.node);
                    }
                }
                reason = "已补充附近模板飞船的电量、流体和测试库存。";
                return true;
            }
            catch (Exception error) { reason = "补充未完成：" + error.Message + "；若已开始补充，之前的资源修改会保留。"; return false; }
        }

        private static JObject ReadMarker(ItemData data)
            => data == null ? null : ItemSpecialDataJsonStore.ReadRoot(data.ItemSpecialData)[TemplateMarker] as JObject;
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 机器部件快照回写
        public void CaptureMachinePieceSnapshot(ShipPieceState piece, MachineEntity node)
        {
            if (piece == null || node == null || piece.PieceId != node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !contents.ContainsKey(piece.PieceId)) throw new InvalidOperationException("机器快照与船体部件身份不匹配。" );
            // 存档捕获会替换冷数据引用，补给必须同时刷新会话内容和部件保存快照。
            ItemData snapshot = MachineWorld.CaptureSnapshot(node) ?? throw new InvalidOperationException("船上机器资源快照保存失败。" );
            contents[piece.PieceId] = snapshot;
            piece.SnapshotJson = Encode(snapshot);
        }
        #endregion
    }
}
