using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 客户端船内世界增量同步
        private sealed class InteriorNetworkSnapshot
        {
            internal readonly Dictionary<string, (ShipState Ship, ShipPieceState Piece)> Previous = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, (ShipState Ship, ShipPieceState Piece, ItemData Data)> Incoming = new(StringComparer.Ordinal);
            internal readonly HashSet<string> AllShips = new(StringComparer.Ordinal);
            internal readonly HashSet<string> Ships = new(StringComparer.Ordinal);
            internal Dictionary<string, IncomingLocalWorld> Worlds;
        }
        private readonly HashSet<int> interiorItemsWithRemoteAuthority = new();

        private InteriorNetworkSnapshot PrepareInteriorNetworkSnapshot(SpaceSessionState received)
        {
            var snapshot = new InteriorNetworkSnapshot();
            if (State != null)
                foreach (ShipState ship in State.Ships)
                    foreach (ShipPieceState piece in ship.Pieces)
                        if (piece.IsAlive) snapshot.Previous.Add(piece.PieceId, (ship, piece));
            foreach (ShipState ship in received.Ships)
            {
                if (ship == null || string.IsNullOrWhiteSpace(ship.ShipId) || !snapshot.AllShips.Add(ship.ShipId))
                    throw new InvalidOperationException("联机船体身份无效或重复。");
                if (ship.IsWreck)
                {
                    if (ship.RetainsInteriorWorld) snapshot.Ships.Add(ship.ShipId);
                    continue;
                }
                snapshot.Ships.Add(ship.ShipId);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    if (!int.TryParse(piece.PieceId, out int id) || string.IsNullOrWhiteSpace(piece.SnapshotJson))
                        throw new InvalidOperationException("联机船件缺少真实物品快照。");
                    ItemData data = Decode(piece.SnapshotJson);
                    if (data == null || data.Guid != id || data.IDName != piece.ItemId ||
                        !snapshot.Incoming.TryAdd(piece.PieceId, (ship, piece, data)))
                        throw new InvalidOperationException("联机船件身份或物品快照不一致。");
                }
            }
            snapshot.Worlds = DecodeNetworkLocalWorldStates(received, snapshot.Ships);
            return snapshot;
        }

        private void RemoveObsoleteInteriorNetworkItems(InteriorNetworkSnapshot snapshot)
        {
            MovePassengersOutOfRemovedShipInteriorWorlds(snapshot.Ships);
            foreach (var pair in snapshot.Previous)
            {
                ShipState oldShip = pair.Value.Ship;
                ShipPieceState oldPiece = pair.Value.Piece;
                bool found = snapshot.Incoming.TryGetValue(pair.Key, out var next);
                bool sameDefinition = found && next.Piece.ItemId == oldPiece.ItemId;
                if (!sameDefinition)
                {
                    if (int.TryParse(oldPiece.PieceId, out int oldId)) interiorItemsWithRemoteAuthority.Remove(oldId);
                    ReleaseShipInteriorItem(oldPiece, capture: false);
                }
                if ((!sameDefinition || next.Ship.ShipId != oldShip.ShipId) &&
                    MachineCatalog.Get(oldPiece.ItemId) != null && int.TryParse(oldPiece.PieceId, out int id))
                    using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(oldShip.ShipId))) MachineWorld.ApplyRemoteRemoval(id);
            }
        }

        private void ApplyInteriorNetworkSnapshot(InteriorNetworkSnapshot snapshot)
        {
            foreach (var pair in snapshot.Worlds)
                if (!GameplayWorldRegistry.TryGet(pair.Key, out _))
                    SaveDataMgr.Instance.PrepareDetachedNetworkLocalWorldChunks(pair.Key, pair.Value.Chunks);
            foreach (ShipState ship in State.Ships)
                if (!ship.IsWreck || ship.RetainsInteriorWorld) EnsureShipInteriorWorld(ship);
            ApplyNetworkLocalWorldStates(snapshot.Worlds);

            ItemMgr manager = ItemMgr.GetExistingInstance();
            foreach (var pair in snapshot.Incoming)
            {
                ShipState ship = pair.Value.Ship;
                ShipPieceState piece = pair.Value.Piece;
                ItemData data = pair.Value.Data;
                ShipInteriorWorldRuntime world = EnsureShipInteriorWorld(ship);
                bool changed = !snapshot.Previous.TryGetValue(pair.Key, out var previous) ||
                    previous.Piece.SnapshotJson != piece.SnapshotJson || previous.Ship.ShipId != ship.ShipId ||
                    previous.Piece.CellX != piece.CellX || previous.Piece.CellY != piece.CellY ||
                    previous.Piece.QuarterTurns != piece.QuarterTurns;
                using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
                if (MachineWorld.OwnsWorldItem(data))
                {
                    ShipDeviceState device = GetDevice(piece.PieceId);
                    MachineEntity node = MachineWorld.GetById(data.Guid);
                    if (changed || node == null) MachineWorld.ApplyRemoteSnapshot(data, device?.Rpm ?? 0f, device?.EntryDirection ?? -1);
                    else MachineWorld.ApplyRemoteSpeed(data.Guid, device?.Rpm ?? 0f, device?.EntryDirection ?? -1);
                    contents[piece.PieceId] = MachineWorld.GetById(data.Guid).Snapshot;
                    continue;
                }

                if (Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) &&
                    !string.IsNullOrWhiteSpace(building.TileBlockId)) continue;
                Item item = null;
                if (manager != null && manager.WorldRunTimeItems.TryGetValue(data.Guid, out Item existing) &&
                    existing != null && ReferenceEquals(existing.PersistenceOwner, this))
                {
                    item = existing;
                    if (GameplayWorldRegistry.GetItemWorldKey(item) != world.WorldKey ||
                        previous.Piece != null && (previous.Piece.CellX != piece.CellX || previous.Piece.CellY != piece.CellY ||
                            previous.Piece.QuarterTurns != piece.QuarterTurns))
                        TransferExistingItemToShipInterior(ship, piece, item);
                    bool remoteAuthority = ItemNetworkStateSerialization.HasCurrentRemoteAuthority(item);
                    bool restoreSnapshot = interiorItemsWithRemoteAuthority.Remove(data.Guid);
                    if (remoteAuthority) interiorItemsWithRemoteAuthority.Add(data.Guid);
                    if (!remoteAuthority && (changed || restoreSnapshot) && (!ItemNetworkStateSerialization.TrySerializeItemData(data, out byte[] payload) ||
                        !ItemNetworkStateSerialization.Apply(item, payload, reloadRuntimeModules: true, requireMatchingGuid: true)))
                        throw new InvalidOperationException("船内物品联机状态更新失败：" + piece.PieceId);
                    BindShipInteriorItemCallbacks(item, ship, piece);
                    contents[piece.PieceId] = item.itemData;
                }
                else EnsureShipInteriorItem(ship, piece, data);
            }

            RebindPassengersToShipInteriorWorlds();
            foreach (string id in new List<string>(shipInteriorWorlds.Keys))
                if (!snapshot.Ships.Contains(id))
                {
                    ReleaseShipInteriorWorldItems(MachineWorld.GetShipScopeKey(id), capture: false);
                    ReleaseShipInteriorWorld(id);
                    MachineWorld.ReleaseShipScope(id);
                }
        }
        #endregion
    }
}
