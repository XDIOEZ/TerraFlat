using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船结构与普通区块地形
        private readonly Dictionary<string, ShipInteriorTerrainProjection> shipInteriorTerrain = new(StringComparer.Ordinal);

        private void SynchronizeShipInteriorTerrain(ShipState ship, ShipInteriorWorldRuntime world, bool force = false)
        {
            if (!shipInteriorTerrain.TryGetValue(ship.ShipId, out var projection))
                shipInteriorTerrain.Add(ship.ShipId, projection = new ShipInteriorTerrainProjection(this, world));
            projection.Synchronize(ship, force);
        }

        private void ReleaseShipInteriorTerrain(string shipId)
        {
            if (shipInteriorTerrain.Remove(shipId, out var projection)) projection.Dispose();
        }

        internal void SynchronizeShipInteriorTileHealth(ShipState ship, ShipPieceState piece)
        {
            if (ship != null && piece != null && shipInteriorTerrain.TryGetValue(ship.ShipId, out var projection))
                projection.SynchronizeTileHealth(piece);
        }

        // 船体只保留结构身份；格墙的显示、碰撞、受损和拆回交给普通地块系统。
        private sealed class ShipInteriorTerrainProjection : IDisposable
        {
            private readonly SpaceSession session;
            private readonly ShipInteriorWorldRuntime world;
            private readonly Dictionary<ShipCell, TileBinding> supports = new();
            private readonly Dictionary<ShipCell, TileBinding> walls = new();
            private readonly Dictionary<ChunkTerrainData, Action<ChunkTerrainChanged>> subscriptions = new();
            private ShipState ship;
            private ulong revision = ulong.MaxValue;
            private bool writing;

            private readonly struct TileBinding
            {
                internal readonly ShipPieceState Piece;
                internal readonly int TileId;
                internal readonly float MaximumTileHealth;
                internal readonly bool RenderedByEntity;
                internal TileBinding(ShipPieceState piece, RuntimeTileDefinition definition, bool renderedByEntity)
                {
                    Piece = piece; TileId = definition.RuntimeTileId; RenderedByEntity = renderedByEntity;
                    MaximumTileHealth = Math.Max(1f, definition.damageProfile?.MaxHealth ?? 1f);
                }
                internal float Damage => MaximumTileHealth * (1f - (float)Math.Clamp(Piece.Health / Math.Max(1d, Piece.MaxHealth), 0d, 1d));
            }

            internal ShipInteriorTerrainProjection(SpaceSession session, ShipInteriorWorldRuntime world)
            { this.session = session; this.world = world; }

            internal void Synchronize(ShipState value, bool force = false)
            {
                if (!force && ReferenceEquals(ship, value) && revision == value.StructureRevision) return;
                ship = value;
                using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
                var nextSupports = new Dictionary<ShipCell, TileBinding>();
                var nextWalls = new Dictionary<ShipCell, TileBinding>();
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    ItemData data = session.GetPieceData(piece);
                    Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building);
                    string tileBlockId = null;
                    if (piece.Kind == ShipPieceKind.Floor)
                        tileBlockId = session.GetDevice(piece.PieceId)?.Configuration?.SupportTileBlockId
                            ?? ResolvePartConfiguration(data)?.SupportTileBlockId ?? building?.TileBlockId;
                    else if (building != null)
                        tileBlockId = building.TileBlockId;
                    if (string.IsNullOrWhiteSpace(tileBlockId)) continue;
                    RuntimeTileDefinition definition = GameRes.ExistingInstance.GetTileBlock(tileBlockId)
                        ?? throw new InvalidOperationException("船内支撑或格墙缺少正式地块定义：" + tileBlockId);
                    var target = piece.Kind == ShipPieceKind.Floor ? nextSupports : nextWalls;
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                        target.Add(cell, new TileBinding(piece, definition, ship.IsWreck || string.IsNullOrWhiteSpace(building?.TileBlockId)));
                }
                writing = true;
                try
                {
                    foreach (var pair in supports)
                        if (!nextSupports.ContainsKey(pair.Key) && TryGetTerrain(pair.Key, out _, out var terrain, out var local) &&
                            TerrainSupportLayer.GetTileId(terrain, local.x, local.y) == pair.Value.TileId)
                            TerrainSupportLayer.Set(terrain, local.x, local.y, 0, 0);
                    foreach (var pair in walls)
                        if (!nextWalls.ContainsKey(pair.Key) && TryGetTerrain(pair.Key, out _, out var terrain, out var local))
                            terrain.TryRemoveBlockingTile(local.x, local.y, pair.Value.TileId);
                    supports.Clear(); walls.Clear();
                    foreach (var pair in nextSupports)
                    {
                        RequireTerrain(pair.Key, out var terrain, out var local);
                        TerrainSupportLayer.Set(terrain, local.x, local.y, pair.Value.TileId, 1,
                            renderedByEntity: pair.Value.RenderedByEntity, externallyOwned: true);
                        supports.Add(pair.Key, pair.Value);
                    }
                    foreach (var pair in nextWalls)
                    {
                        RequireTerrain(pair.Key, out var terrain, out var local);
                        int existing = terrain.GetCell(local.x, local.y).BlockingTileId;
                        if (existing != 0 && existing != pair.Value.TileId ||
                            existing != pair.Value.TileId && !terrain.TrySetBlockingTile(local.x, local.y, pair.Value.TileId))
                            throw new InvalidOperationException("船内格墙与实际地形占用冲突：" + pair.Key.X + "," + pair.Key.Y);
                        terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, local.x, local.y,
                            pair.Value.Damage);
                        walls.Add(pair.Key, pair.Value);
                    }
                }
                finally { writing = false; }
                foreach (ChunkRuntime chunk in world.Chunks.Chunks.Values)
                {
                    ChunkTerrainData terrain = chunk.Terrain;
                    if (terrain == null || subscriptions.ContainsKey(terrain)) continue;
                    Action<ChunkTerrainChanged> handler = change => OnTerrainChanged(chunk, change);
                    subscriptions.Add(terrain, handler);
                    terrain.Changed += handler;
                }
                revision = ship.StructureRevision;
            }

            internal void SynchronizeTileHealth(ShipPieceState piece)
            {
                using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
                writing = true;
                try
                {
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                        if (walls.TryGetValue(cell, out var binding) && binding.Piece == piece &&
                            TryGetTerrain(cell, out _, out var terrain, out var local))
                        {
                            if (!piece.IsAlive) terrain.TryRemoveBlockingTile(local.x, local.y, binding.TileId);
                            else terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, local.x, local.y,
                                binding.Damage);
                        }
                }
                finally { writing = false; }
            }

            private void OnTerrainChanged(ChunkRuntime chunk, ChunkTerrainChanged change)
            {
                bool supportChanged = change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer);
                if (writing || !FlatWorld.Networking.GameNetwork.HasStateAuthority ||
                    change.Kind != TerrainChangeKind.Cell && change.Kind != TerrainChangeKind.TileStack &&
                    !change.AffectsEnvironmentLayer(TileBuildingSystem.RuntimeDamageLayerId) && !supportChanged) return;
                using var scope = GameplayWorldRegistry.UseWorld(world.WorldKey);
                Int2 origin = chunk.Address.ChunkOrigin;
                foreach (int index in change.GetCellIndices(chunk.Terrain.Width))
                {
                    int x = index % chunk.Terrain.Width, y = index / chunk.Terrain.Width;
                    var cell = new ShipCell(origin.X + x, origin.Y + y);
                    if (supportChanged && supports.TryGetValue(cell, out var floor) && floor.Piece.IsAlive &&
                        TerrainSupportLayer.GetTileId(chunk.Terrain, x, y) != floor.TileId)
                    {
                        floor.Piece.Health = 0; floor.Piece.Destroyed = true;
                        session.RollbackPiece(ship, floor.Piece, deferEmptyWorldRetirement: true);
                    }
                    if (!walls.TryGetValue(cell, out var binding) || !binding.Piece.IsAlive) continue;
                    ShipPieceState piece = binding.Piece;
                    if (chunk.Terrain.GetCell(x, y).BlockingTileId != binding.TileId)
                    {
                        piece.Health = 0d;
                        piece.Destroyed = true;
                        ship.AtmosphereStructureDirty = true;
                        session.RollbackPiece(ship, piece, deferEmptyWorldRetirement: true);
                    }
                    else
                    {
                        chunk.Terrain.TryGetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, x, y, out float damage);
                        piece.Health = piece.MaxHealth * Math.Clamp(1d - damage / binding.MaximumTileHealth, 0d, 1d);
                    }
                }
            }

            private bool TryGetTerrain(ShipCell cell, out ChunkRuntime chunk, out ChunkTerrainData terrain, out Vector2Int local)
            {
                var address = world.Chunks.ResolveWorldAddress(new Vector2(cell.X + .5f, cell.Y + .5f));
                if (world.Chunks.TryGetChunkRuntime(address, out chunk) && chunk.Terrain is { IsDisposed: false } ready)
                {
                    terrain = ready;
                    local = new Vector2Int(cell.X - address.ChunkOrigin.X, cell.Y - address.ChunkOrigin.Y);
                    return true;
                }
                terrain = null; local = default;
                return false;
            }

            private void RequireTerrain(ShipCell cell, out ChunkTerrainData terrain, out Vector2Int local)
            {
                if (!TryGetTerrain(cell, out _, out terrain, out local))
                    throw new InvalidOperationException("船内地形投影前必须先提交所属区块。");
            }

            public void Dispose()
            {
                foreach (var pair in subscriptions) pair.Key.Changed -= pair.Value;
                subscriptions.Clear(); supports.Clear(); walls.Clear();
            }
        }
        #endregion
    }
}
