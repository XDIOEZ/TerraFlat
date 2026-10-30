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
            GameplayTestSystemProbe.Register("networking", "terrain_wire", CaptureWire);
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
                }
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
            };
        }
#endif
        #endregion

        #region 正式地形复制采样
        private static ChunkTerrainData CreateTerrain()
        {
            using var buffer = new ChunkTerrainBuffer(2, 1);
            for (int x = 0; x < 2; x++)
                buffer.SetCell(x, 0, new TerrainCell(17, 0, 0, 1, 25, TerrainCellFlags.None));
            buffer.SetLiquid(0, 0, 1, 8);
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
            result["partialAccepted"] = cursor.Apply(10, 7, 2, new[] { platform }, 10, new[] { FireCell(0, .75f) });
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
            result["duplicateAccepted"] = cursor.Apply(10, 7, 2, new[] { platform }, 10, new[] { FireCell(0, .75f) });
            result["duplicateWrites"] = changed;
            TerrainSupportLayer.Set(server, 0, 0, 0, 0);
            TerrainBuildingCellState removed = TerrainBuildingCellState.Capture(server, 0);
            result["removeAccepted"] = cursor.Apply(12, 0, 2, new[] { removed }, 12, new[] { FireCell(0, 0f) });
            cursor.Apply(11, 7, 2, new[] { platform, other }, 11,
                new[] { FireCell(0, .75f), FireCell(1, 0f) });
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
            cursor.Apply(15, 9, 2, new[] { platform }, 15, new[] { FireCell(0, .4f) });
            cursor.Apply(15, 9, 2, new[] { platform }, 15, new[] { FireCell(0, .4f) });
            result["duplicateCompleted"] = cursor.HasFullState;
            cursor.Apply(15, 9, 2, new[] { wall }, 15, new[] { FireCell(1, 0f) });
            result["refreshedFull"] = cursor.HasFullState;
            using ChunkTerrainData reloaded = CreateTerrain();
            var reloadedCursor = new NetworkTerrainChunkCursor(reloaded);
            result["reloadedFull"] = reloadedCursor.HasFullState;
            result["oldChunkTokenAccepted"] = reloadedCursor.Apply(15, 9, 2, new[] { platform, wall }, 15,
                new[] { FireCell(0, .4f), FireCell(1, 0f) });
            reloadedCursor.BeginSnapshot(10);
            reloaded.SetFireStatesBatch(new[]
            {
                new WorldFireCellState { CellIndex = 0, Intensity = .8f, SurfaceFuelBurnSeconds = 19f }
            });
            reloadedCursor.Apply(16, 10, 2, new[] { removed, other }, 16,
                new[] { FireCell(0, 0f), FireCell(1, .35f) });
            result["reloadedSupport"] = TerrainSupportLayer.GetTileId(reloaded, 0, 0);
            result["reloadedWall"] = reloaded.GetCell(1, 0).BlockingTileId;
            result["reloadedComplete"] = reloadedCursor.HasFullState;
            result["snapshotZeroClearedFire"] = reloaded.GetFireState(0, 0).Intensity == 0f;
            result["snapshotFireIntensity"] = reloaded.GetFireState(1, 0).Intensity;
            return result;
        }
        #endregion
    }
}
#endif
