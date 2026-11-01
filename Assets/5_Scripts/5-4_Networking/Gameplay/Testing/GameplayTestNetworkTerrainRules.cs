#if UNITY_EDITOR || DEVELOPMENT_BUILD || TERRAIN_STANDALONE_TESTS
using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
#if !TERRAIN_STANDALONE_TESTS
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;
#endif

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>直接采样正式格子状态和客户端游标；不创建或接管用户联机会话。</summary>
    public static class GameplayTestNetworkTerrainRules
    {
        #region 规则登记
#if !TERRAIN_STANDALONE_TESTS
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register()
        {
            GameplayTestSystemProbe.Register("networking", "terrain_replication", _ => JObject.FromObject(Capture()));
            GameplayTestSystemProbe.Register("networking", "terrain_liquid_replication", _ => JObject.FromObject(CaptureLiquidReplication()));
            GameplayTestSystemProbe.Register("networking", "terrain_wire", CaptureWire);
            GameplayTestSystemProbe.Register("networking", "terrain_packet_splitting", CaptureTerrainPacketSplitting);
            GameplayTestSystemProbe.Register("networking", "weather_surface_ice_merge",
                NetworkWeatherStateCoordinator.CaptureWeatherSurfaceIceMerge);
        }

        private static JObject CaptureWire(JObject args)
        {
            using ChunkTerrainData terrain = CreateTerrain();
            TerrainSupportLayer.Set(terrain, 0, 0, 91, 1);
            terrain.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 0, Intensity = .75f, SurfaceFuelBurnSeconds = 42f }
            });
            var message = new NetworkTerrainChunkMessage
            {
                WorldKey = "probe-world", DimensionId = "surface", RealmRevision = 7, RequestToken = 3,
                ChunkX = -16, ChunkY = 32, TotalCells = 2, Revision = 1234567890123L,
                Cells = new[] { TerrainBuildingCellState.Capture(terrain, 0), TerrainBuildingCellState.Capture(terrain, 1) },
                FireRevision = 1234567890124L,
                FireCells = new[]
                {
                    new NetworkTerrainFireCellState { Index = 0, Intensity = .75f },
                    new NetworkTerrainFireCellState { Index = 1, Intensity = 0f }
                },
                LiquidCells = new[] { NetworkTerrainLiquidCellState.Capture(terrain, 0),
                    NetworkTerrainLiquidCellState.Capture(terrain, 1) }
            };
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.Write(message);
            using NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment());
            NetworkTerrainChunkMessage decoded = reader.Read<NetworkTerrainChunkMessage>();
            return new JObject
            {
                ["protocol"] = NetworkGameplayProtocol.CurrentVersion, ["remainingBytes"] = reader.Remaining,
                ["world"] = decoded.WorldKey, ["dimension"] = decoded.DimensionId,
                ["realm"] = decoded.RealmRevision, ["token"] = decoded.RequestToken,
                ["chunkX"] = decoded.ChunkX, ["chunkY"] = decoded.ChunkY,
                ["revision"] = decoded.Revision, ["cellCount"] = decoded.Cells.Length,
                ["support"] = decoded.Cells[0].SupportTileId, ["baseGround"] = decoded.Cells[0].GroundTileId,
                ["fireRevision"] = decoded.FireRevision, ["fireCellCount"] = decoded.FireCells.Length,
                ["fireIntensity"] = decoded.FireCells[0].Intensity, ["explicitZeroFire"] = decoded.FireCells[1].Intensity == 0f,
                ["liquidCellCount"] = decoded.LiquidCells.Length, ["liquidId"] = decoded.LiquidCells[0].LiquidId,
                ["liquidLevel"] = decoded.LiquidCells[0].Level,
                ["explicitZeroLiquid"] = decoded.LiquidCells[1].Level == 0 && string.IsNullOrEmpty(decoded.LiquidCells[1].LiquidId)
            };
        }

        private static JObject CaptureTerrainPacketSplitting(JObject args)
        {
            const int count = 8, limit = 512;
            var cells = new TerrainBuildingCellState[count];
            var fire = new NetworkTerrainFireCellState[count];
            var liquids = new NetworkTerrainLiquidCellState[count];
            for (int i = 0; i < count; i++)
            {
                cells[i] = new TerrainBuildingCellState { Index = i };
                fire[i] = new NetworkTerrainFireCellState { Index = i };
                liquids[i] = new NetworkTerrainLiquidCellState { Index = i, Level = 80,
                    LiquidId = "mod:" + new string('x', 180) + i };
            }
            var header = new NetworkTerrainChunkMessage
            {
                WorldKey = "probe-world", DimensionId = "surface", RequestToken = 3,
                TotalCells = count, Revision = 10, FireRevision = 10
            };
            int parts = 0, terrainCount = 0, fireCount = 0, liquidCount = 0;
            bool fits = true, aligned = true;
            foreach (NetworkTerrainChunkMessage batch in NetworkItemStateCoordinator.BuildTerrainCellBatches(
                         header, cells, fire, liquids, limit))
            {
                parts++;
                fits &= NetworkItemStateCoordinator.TerrainMessageFitsTransport(batch, limit);
                aligned &= batch.Cells.Length == batch.FireCells.Length && batch.Cells.Length == batch.LiquidCells.Length;
                terrainCount += batch.Cells.Length; fireCount += batch.FireCells.Length; liquidCount += batch.LiquidCells.Length;
            }
            return new JObject { ["parts"] = parts, ["fits"] = fits, ["aligned"] = aligned,
                ["terrainCount"] = terrainCount, ["fireCount"] = fireCount, ["liquidCount"] = liquidCount };
        }
#endif
        #endregion

        #region 正式地形复制采样
        private static ChunkTerrainData CreateTerrain()
        {
            using var buffer = new ChunkTerrainBuffer(2, 1);
            for (int x = 0; x < 2; x++)
                buffer.SetCell(x, 0, new TerrainCell(17, 0, 0, 1, 25, TerrainCellFlags.None));
            buffer.SetLiquid(0, 0, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 8);
            return buffer.Seal();
        }

        public static Dictionary<string, object> Capture()
        {
            using ChunkTerrainData server = CreateTerrain();
            using ChunkTerrainData client = CreateTerrain();
            using ChunkTerrainData fireProgressProbe = CreateTerrain();
            client.SetEnvironmentValue("probe.weather", 0, 0, 45f);
            var cursor = new NetworkTerrainChunkCursor(client);
            var result = new Dictionary<string, object>();
            static NetworkTerrainFireCellState FireCell(int index, float intensity)
                => new() { Index = index, Intensity = intensity };
            int fireNotifications = 0;
            bool intensityAffectsPresentation = false, progressAffectsPresentation = true;
            fireProgressProbe.Changed += changed =>
            {
                if (changed.Kind != TerrainChangeKind.Fire) return;
                fireNotifications++;
                if (fireNotifications == 1) intensityAffectsPresentation = changed.AffectsFirePresentation;
                else progressAffectsPresentation = changed.AffectsFirePresentation;
            };
            fireProgressProbe.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 0, Intensity = .5f, SurfaceFuelBurnSeconds = 1f }
            });
            fireProgressProbe.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 0, Intensity = .5f, SurfaceFuelBurnSeconds = 2f }
            });
            result["fireIntensityAffectsPresentation"] = intensityAffectsPresentation;
            result["fireProgressOnlyStillNotifies"] = fireNotifications == 2;
            result["fireProgressOnlySkipsPresentation"] = !progressAffectsPresentation;
            cursor.BeginSnapshot(7);
            TerrainSupportLayer.Set(server, 0, 0, 91, 1);
            TerrainBuildingCellState platform = TerrainBuildingCellState.Capture(server, 0);
            TerrainBuildingCellState other = TerrainBuildingCellState.Capture(server, 1);
            var liquid0 = NetworkTerrainLiquidCellState.Capture(server, 0);
            var liquid1 = NetworkTerrainLiquidCellState.Capture(server, 1);
            result["partialAccepted"] = cursor.Apply(10, 7, 2, new[] { platform }, 10,
                new[] { FireCell(0, .75f) }, new[] { liquid0 });
            result["partialFull"] = cursor.HasFullState;
            result["partialFireIntensity"] = client.GetFireState(0, 0).Intensity;
            result["fireProgressExcluded"] = client.GetFireState(0, 0).SurfaceFuelBurnSeconds == 0f;
            result["support"] = TerrainSupportLayer.GetTileId(client, 0, 0);
            result["baseGround"] = client.GetCell(0, 0).GroundTileId;
            result["baseCost"] = client.GetCell(0, 0).NavigationCost;
            result["liquid"] = client.GetLiquidLevel(0, 0);
            result["effectiveGround"] = TerrainSupportLayer.GetSurfaceCell(client, 0, 0).GroundTileId;
            result["walkable"] = TerrainSupportLayer.GetSurfaceCell(client, 0, 0).IsWalkable;
            int changed = 0;
            client.Changed += _ => changed++;
            result["duplicateAccepted"] = cursor.Apply(10, 7, 2, new[] { platform }, 10,
                new[] { FireCell(0, .75f) }, new[] { liquid0 });
            result["duplicateWrites"] = changed;
            TerrainSupportLayer.Set(server, 0, 0, 0, 0);
            TerrainBuildingCellState removed = TerrainBuildingCellState.Capture(server, 0);
            result["removeAccepted"] = cursor.Apply(12, 0, 2, new[] { removed }, 12, new[] { FireCell(0, 0f) });
            cursor.Apply(11, 7, 2, new[] { platform, other }, 11,
                new[] { FireCell(0, .75f), FireCell(1, 0f) }, new[] { liquid0, liquid1 });
            result["oldSnapshotSupport"] = TerrainSupportLayer.GetTileId(client, 0, 0);
            result["oldSnapshotFireIntensity"] = client.GetFireState(0, 0).Intensity;
            result["fireProgressExcluded"] = client.GetFireState(0, 0).SurfaceFuelBurnSeconds == 0f;
            result["fireOnlyAccepted"] = cursor.Apply(13, 0, 2, Array.Empty<TerrainBuildingCellState>(),
                13, new[] { FireCell(1, .33f) });
            result["fireOnlyIntensity"] = client.GetFireState(1, 0).Intensity;
            result["fullAfterReorder"] = cursor.HasFullState;
            result["removedWalkable"] = TerrainSupportLayer.GetSurfaceCell(client, 0, 0).IsWalkable;
            result["removedLiquid"] = client.GetLiquidLevel(0, 0);
            client.TryGetEnvironmentValue("probe.weather", 0, 0, out float weather);
            result["weather"] = weather;
            server.TrySetBlockingTile(1, 0, 74);
            server.SetEnvironmentValue(TerrainBuildingCellState.DamageLayerId, 1, 0, 9f);
            var wall = TerrainBuildingCellState.Capture(server, 1);
            result["wallAccepted"] = cursor.Apply(13, 0, 2, new[] { wall });
            result["wall"] = client.GetCell(1, 0).BlockingTileId;
            client.TryGetEnvironmentValue(TerrainBuildingCellState.DamageLayerId, 1, 0, out float damage);
            result["damage"] = damage;
            cursor.Apply(12, 0, 2, new[] { other });
            result["oldDeltaWall"] = client.GetCell(1, 0).BlockingTileId;
            var invalid = wall;
            invalid.Index = 2;
            result["invalidAccepted"] = cursor.Apply(14, 0, 2, new[] { platform, invalid });
            result["atomicSupport"] = TerrainSupportLayer.GetTileId(client, 0, 0);
            invalid = wall; invalid.Damage = float.NaN;
            result["nanAccepted"] = cursor.Apply(14, 0, 2, new[] { invalid });
            result["wrongSizeAccepted"] = cursor.Apply(14, 0, 3, new[] { wall });
            result["wrongTokenAccepted"] = cursor.Apply(14, 8, 2, new[] { platform });
            result["zeroRevisionAccepted"] = cursor.Apply(0, 0, 2, new[] { platform });
            result["oversizeAccepted"] = cursor.Apply(14, 0, 2, new TerrainBuildingCellState[65]);
            cursor.RequireSnapshot();
            result["invalidatedFull"] = cursor.HasFullState;
            cursor.BeginSnapshot(9);
            cursor.Apply(15, 9, 2, new[] { platform }, 15, new[] { FireCell(0, .4f) }, new[] { liquid0 });
            cursor.Apply(15, 9, 2, new[] { platform }, 15, new[] { FireCell(0, .4f) }, new[] { liquid0 });
            result["duplicateCompleted"] = cursor.HasFullState;
            cursor.Apply(15, 9, 2, new[] { wall }, 15, new[] { FireCell(1, 0f) }, new[] { liquid1 });
            result["refreshedFull"] = cursor.HasFullState;
            using ChunkTerrainData reloaded = CreateTerrain();
            var reloadedCursor = new NetworkTerrainChunkCursor(reloaded);
            result["reloadedFull"] = reloadedCursor.HasFullState;
            result["oldChunkTokenAccepted"] = reloadedCursor.Apply(15, 9, 2, new[] { platform, wall }, 15,
                new[] { FireCell(0, .4f), FireCell(1, 0f) }, new[] { liquid0, liquid1 });
            reloadedCursor.BeginSnapshot(10);
            reloaded.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 0, Intensity = .8f, SurfaceFuelBurnSeconds = 19f }
            });
            reloadedCursor.Apply(16, 10, 2, new[] { removed, other }, 16,
                new[] { FireCell(0, 0f), FireCell(1, .35f) }, new[] { liquid0, liquid1 });
            result["reloadedSupport"] = TerrainSupportLayer.GetTileId(reloaded, 0, 0);
            result["reloadedWall"] = reloaded.GetCell(1, 0).BlockingTileId;
            result["reloadedComplete"] = reloadedCursor.HasFullState;
            result["snapshotZeroClearedFire"] = reloaded.GetFireState(0, 0).Intensity == 0f;
            result["snapshotFireIntensity"] = reloaded.GetFireState(1, 0).Intensity;
            return result;
        }

        // 使用真实游标验证液体与地形独立排序及完整快照。
        public static Dictionary<string, object> CaptureLiquidReplication()
        {
            using ChunkTerrainData server = CreateTerrain();
            using ChunkTerrainData client = CreateTerrain();
            server.SetLiquid(0, 0, 0, 0);
            client.SetLiquid(0, 0, 0, 0);
            int sea = server.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId);
            int fresh = server.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
            server.SetCell(0, 0, new TerrainCell(3, 0, 0, 0, 3, TerrainCellFlags.Walkable));
            server.SetLiquid(0, 0, sea, 80);
            var ground0 = TerrainBuildingCellState.Capture(server, 0);
            var ground1 = TerrainBuildingCellState.Capture(server, 1);
            var frozenSnapshot0 = NetworkTerrainLiquidCellState.Capture(server, 0);
            var frozenSnapshot1 = NetworkTerrainLiquidCellState.Capture(server, 1);
            var cursor = new NetworkTerrainChunkCursor(client);
            int batches = 0;
            client.LiquidBatchChanged += _ => batches++;
            bool thawAccepted = cursor.Apply(5, 0, 2, new[] { ground0 }, liquidCells: new[] { frozenSnapshot0 });
            bool thawed = client.GetCell(0, 0).GroundTileId == 3 &&
                client.GetLiquidId(0, 0) == LiquidTypeCatalog.SeaWaterId && client.GetLiquidLevel(0, 0) == 80;
            int thawBatches = batches;

            cursor.BeginSnapshot(7);
            server.SetLiquid(0, 0, sea, 35);
            var newer = NetworkTerrainLiquidCellState.Capture(server, 0);
            cursor.Apply(8, 0, 2, Array.Empty<TerrainBuildingCellState>(), liquidCells: new[] { newer });
            bool snapshotAccepted = cursor.Apply(7, 7, 2, new[] { ground0, ground1 }, 7,
                new[] { new NetworkTerrainFireCellState { Index = 0 }, new NetworkTerrainFireCellState { Index = 1 } },
                new[] { frozenSnapshot0, frozenSnapshot1 });
            bool snapshotKeptNewer = client.GetLiquidLevel(0, 0) == 35 && client.GetLiquidLevel(1, 0) == 0;
            bool snapshotComplete = cursor.HasFullState;

            server.SetLiquid(0, 0, 0, 0);
            server.SetLiquid(1, 0, fresh, 37);
            int beforeBatch = batches;
            bool batchAccepted = cursor.Apply(9, 0, 2, Array.Empty<TerrainBuildingCellState>(), liquidCells: new[]
            {
                NetworkTerrainLiquidCellState.Capture(server, 1), NetworkTerrainLiquidCellState.Capture(server, 0)
            });
            bool clearedAndFresh = client.GetLiquidLevel(0, 0) == 0 &&
                client.GetLiquidId(1, 0) == LiquidTypeCatalog.DirtyWaterId && client.GetLiquidLevel(1, 0) == 37;
            bool oneBatch = batches == beforeBatch + 1;

            ground0.GroundTileId = 4;
            cursor.Apply(25, 0, 2, new[] { ground0 });
            server.SetLiquid(0, 0, sea, 20);
            bool independent = cursor.Apply(10, 0, 2, Array.Empty<TerrainBuildingCellState>(),
                liquidCells: new[] { NetworkTerrainLiquidCellState.Capture(server, 0) }) &&
                client.GetCell(0, 0).GroundTileId == 4 && client.GetLiquidLevel(0, 0) == 20;
            ground0.GroundTileId = 77;
            var unknown = new NetworkTerrainLiquidCellState { Index = 1, LiquidId = "mod:missing", Level = 2 };
            bool unknownRejected = !cursor.Apply(26, 0, 2, new[] { ground0 }, liquidCells: new[] { unknown });
            bool duplicateRejected = !cursor.Apply(26, 0, 2, new[] { ground0 }, liquidCells: new[] { newer, newer });
            bool badLevelRejected = !cursor.Apply(26, 0, 2, new[] { ground0 }, liquidCells: new[]
                { new NetworkTerrainLiquidCellState { Index = 1, LiquidId = LiquidTypeCatalog.DirtyWaterId, Level = 101 } });
            bool atomic = client.GetCell(0, 0).GroundTileId == 4 && client.GetLiquidLevel(1, 0) == 37;

            using ChunkTerrainData reloaded = CreateTerrain();
            reloaded.SetLiquid(0, 0, reloaded.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId), 65);
            var reloadedCursor = new NetworkTerrainChunkCursor(reloaded);
            reloadedCursor.SetPersistenceBaseline(100);
            reloadedCursor.Apply(99, 0, 2, Array.Empty<TerrainBuildingCellState>(), liquidCells: new[] { frozenSnapshot0 });
            bool baselineKept = reloaded.GetLiquidLevel(0, 0) == 65;
            reloadedCursor.Apply(101, 0, 2, Array.Empty<TerrainBuildingCellState>(), liquidCells: new[] { newer });
            bool reloadedUpdate = reloaded.GetLiquidId(0, 0) == LiquidTypeCatalog.SeaWaterId && reloaded.GetLiquidLevel(0, 0) == 35;
            return new Dictionary<string, object>
            {
                ["thawAccepted"] = thawAccepted, ["thawed"] = thawed, ["thawBatches"] = thawBatches,
                ["snapshotAccepted"] = snapshotAccepted, ["snapshotKeptNewer"] = snapshotKeptNewer,
                ["snapshotComplete"] = snapshotComplete, ["batchAccepted"] = batchAccepted,
                ["clearedAndFresh"] = clearedAndFresh, ["oneBatch"] = oneBatch, ["independent"] = independent,
                ["unknownRejected"] = unknownRejected, ["duplicateRejected"] = duplicateRejected,
                ["badLevelRejected"] = badLevelRejected, ["atomic"] = atomic,
                ["baselineKept"] = baselineKept, ["reloadedUpdate"] = reloadedUpdate
            };
        }
        #endregion
    }

#if !TERRAIN_STANDALONE_TESTS
    public sealed partial class NetworkWeatherStateCoordinator
    {
        #region 天气地表合并采样
        internal static JObject CaptureWeatherSurfaceIceMerge(JObject args)
        {
            var root = new GameObject("GameplayTest Weather Surface Merge") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var coordinator = root.AddComponent<NetworkWeatherStateCoordinator>();
            var address = new FlatWorld.WorldModel.WorldAddress("probe:weather", new Int2(0, 0));
            WeatherSurfaceDelta Snow(float progress, float depth)
            {
                var delta = new WeatherSurfaceDelta();
                delta.Cells.Add(new WeatherSurfaceCellDelta
                {
                    LocalPosition = Vector2Int.zero, SnowChanged = true,
                    IceMeltProgress = progress, Depth = depth
                });
                return delta;
            }
            try
            {
                coordinator.QueueWeatherSurface(address, 1, Snow(.2f, .1f), true);
                coordinator.QueueWeatherSurface(address, 2, Snow(.9f, .4f), false);
                var soil = new WeatherSurfaceDelta();
                soil.Cells.Add(new WeatherSurfaceCellDelta
                {
                    LocalPosition = Vector2Int.zero, SoilChanged = true, Water = 5
                });
                soil.Cells.Add(new WeatherSurfaceCellDelta
                {
                    LocalPosition = Vector2Int.right, SnowChanged = true, IceMeltProgress = .6f
                });
                coordinator.QueueWeatherSurface(address, 3, soil, false);
                coordinator.QueueWeatherSurface(address, 2, Snow(.1f, 0f), false);
                WeatherSurfaceCellDelta merged = coordinator.pendingSurfaces[address].Cells[Vector2Int.zero];
                float mergedProgress = merged.IceMeltProgress, mergedDepth = merged.Depth;
                bool soilPreserved = merged.SoilChanged && merged.Water == 5;
                int queuedCells = coordinator.pendingSurfaces[address].Cells.Count;
                coordinator.QueueWeatherSurface(address, 4, Snow(1f, 0f), true);
                coordinator.QueueWeatherSurface(address, 5, Snow(.35f, .2f), false);
                WeatherSurfaceCellDelta rollback = coordinator.pendingSurfaces[address].Cells[Vector2Int.zero];
                return new JObject
                {
                    ["mergedProgress"] = mergedProgress, ["mergedDepth"] = mergedDepth,
                    ["soilPreserved"] = soilPreserved, ["rollbackProgress"] = rollback.IceMeltProgress,
                    ["rollbackDepth"] = rollback.Depth, ["fullState"] = coordinator.pendingSurfaces[address].FullState,
                    ["revision"] = coordinator.pendingSurfaces[address].Revision,
                    ["queuedCells"] = queuedCells, ["snapshotCells"] = coordinator.pendingSurfaces[address].Cells.Count
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        #endregion
    }
#endif
}
#endif
