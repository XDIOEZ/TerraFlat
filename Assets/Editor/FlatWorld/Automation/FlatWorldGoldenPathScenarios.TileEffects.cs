using System;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实世界区块上验证玩家进出水体时的地块接收与 Buff 生命周期。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const string WaterSlowBuffId = "水体减速";
        private const string WetBuffId = "潮湿";
        private const float TileEffectTolerance = 0.0001f;

        private static Player _tileEffectPlayer;
        private static Mover _tileEffectMover;
        private static TileEffectReceiver _tileEffectReceiver;
        private static Vector2 _tileEffectOriginalPosition;
        private static Vector2 _tileEffectOriginalVelocity;
        private static bool _tileEffectPositionCaptured;
        private static bool _tileEffectScenarioCompleted;

        #region 生命周期

        private static void ResetRuntimeTileEffectScenario()
        {
            _tileEffectPlayer = null;
            _tileEffectMover = null;
            _tileEffectReceiver = null;
            _tileEffectOriginalPosition = default;
            _tileEffectOriginalVelocity = default;
            _tileEffectPositionCaptured = false;
            _tileEffectScenarioCompleted = false;
        }

        private static void VerifyRuntimeTileEffectAtChunkReady(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (_tileEffectScenarioCompleted)
                return;

            ChunkMgr manager = ChunkMgr.Instance;
            if (manager == null || !TryFindWaterAndDryCells(manager,
                    out Vector2 waterPosition, out Vector2 dryPosition))
                return;

            Player player = context.Player;
            Mover mover = context.Mover;
            if (player?.itemMods == null || mover?.Speed == null)
                throw new InvalidOperationException("地块效果黄金路径找不到真实玩家或移动速度模块。");

            TileEffectReceiver receiver =
                player.itemMods.GetMod_ByID<TileEffectReceiver>(ModText.TileEffectReceiver) ??
                player.GetComponentInChildren<TileEffectReceiver>(true);
            BuffManager buffManager = player.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
            if (receiver == null || buffManager == null)
                throw new InvalidOperationException("真实玩家缺少 TileEffectReceiver 或 BuffManager。");

            float slowdownFactor = ResolveWaterSlowdownFactor();
            CaptureTileEffectPosition(player, mover, receiver);
            try
            {
                MovePlayerForTileEffectCheck(dryPosition);
                if (!receiver.RefreshCurrentTileEffects())
                    throw new InvalidOperationException("TileEffectReceiver 无法绑定运行时陆地地块。");
                if (buffManager.HasBuff(WaterSlowBuffId) || buffManager.HasBuff(WetBuffId))
                    throw new InvalidOperationException("进入水体前玩家已残留水体 Buff。");

                float drySpeedMultiplier = mover.Speed.MultiplicativeModifier;
                MovePlayerForTileEffectCheck(waterPosition);
                if (!receiver.RefreshCurrentTileEffects() ||
                    receiver.currentTileData is not TileData_Water)
                    throw new InvalidOperationException("TileEffectReceiver 未识别运行时水体地块。");
                if (!buffManager.HasBuff(WaterSlowBuffId) || !buffManager.HasBuff(WetBuffId))
                    throw new InvalidOperationException("玩家进入运行时水体后未获得减速与潮湿 Buff。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier -
                              drySpeedMultiplier * slowdownFactor) > TileEffectTolerance)
                {
                    throw new InvalidOperationException(
                        $"水体减速倍率异常：陆地={drySpeedMultiplier:0.###}，" +
                        $"水中={mover.Speed.MultiplicativeModifier:0.###}，" +
                        $"配置倍率={slowdownFactor:0.###}。");
                }

                MovePlayerForTileEffectCheck(dryPosition);
                receiver.RefreshCurrentTileEffects();
                if (buffManager.HasBuff(WaterSlowBuffId) || buffManager.HasBuff(WetBuffId))
                    throw new InvalidOperationException("玩家离开运行时水体后水体 Buff 未移除。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier - drySpeedMultiplier) >
                    TileEffectTolerance)
                    throw new InvalidOperationException("玩家离开水体后移动速度倍率未恢复。");

                _tileEffectScenarioCompleted = true;
                Debug.Log("[GoldenPath][TileEffects] 运行时水体进入、减速、潮湿及离开恢复验证通过。");
            }
            finally
            {
                RestoreTileEffectPosition();
            }
        }

        private static void AssertRuntimeTileEffectScenarioCompleted()
        {
            if (!_tileEffectScenarioCompleted)
                throw new InvalidOperationException("完整移动流程结束前未找到并验证运行时水体地块效果。");
        }

        private static void CleanupRuntimeTileEffectScenario()
        {
            RestoreTileEffectPosition();
            _tileEffectPlayer = null;
            _tileEffectMover = null;
            _tileEffectReceiver = null;
        }

        #endregion

        #region 运行时采样

        private static bool TryFindWaterAndDryCells(ChunkMgr manager,
            out Vector2 waterPosition, out Vector2 dryPosition)
        {
            waterPosition = default;
            dryPosition = default;
            bool foundWater = false;
            bool foundDry = false;

            foreach (ChunkRuntime chunk in manager.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk?.Terrain;
                if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
                    chunk.SimulationStatus != ChunkSimulationStatus.Active || terrain == null)
                    continue;

                for (int y = 0; y < terrain.Height && (!foundWater || !foundDry); y++)
                for (int x = 0; x < terrain.Width && (!foundWater || !foundDry); x++)
                {
                    TerrainCell cell = terrain.GetCell(x, y);
                    bool isWater = (cell.Flags & TerrainCellFlags.Water) != 0;
                    bool isDryCandidate = !isWater && terrain.IsWalkable(x, y);
                    if (isWater ? foundWater : foundDry || !isDryCandidate)
                        continue;

                    var localCell = new Vector2Int(x, y);
                    var worldCell = new Vector2Int(
                        chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
                    if (!ChunkRuntimeTileEffectResolver.TryCreateTileEffectData(
                            manager.ActiveGenerationProfile, terrain, localCell, worldCell,
                            out TileData tileData, out _))
                        continue;

                    Vector2 center = worldCell + new Vector2(0.5f, 0.5f);
                    if (isWater && tileData is TileData_Water)
                    {
                        waterPosition = center;
                        foundWater = true;
                    }
                    else if (isDryCandidate && tileData is not TileData_Water)
                    {
                        dryPosition = center;
                        foundDry = true;
                    }
                }

                if (foundWater && foundDry)
                    return true;
            }

            return false;
        }

        private static float ResolveWaterSlowdownFactor()
        {
            BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(WaterSlowBuffId);
            if (definition == null)
                throw new InvalidOperationException($"水体减速 Buff 未注册：{WaterSlowBuffId}");

            for (int i = 0; i < definition.StartEffects.Count; i++)
            {
                BuffEffectDefinition effect = definition.StartEffects[i];
                if (effect.TypeId == BuffEffectTypeIds.MoveSpeedMultiplier && effect.Value > 0f)
                    return effect.Value;
            }

            throw new InvalidOperationException("水体减速 Buff 缺少有效的移动速度倍率效果。");
        }

        #endregion

        #region 玩家位置恢复

        private static void CaptureTileEffectPosition(Player player, Mover mover,
            TileEffectReceiver receiver)
        {
            _tileEffectPlayer = player;
            _tileEffectMover = mover;
            _tileEffectReceiver = receiver;
            _tileEffectOriginalPosition = mover.rb != null
                ? mover.rb.position
                : (Vector2)player.transform.position;
            _tileEffectOriginalVelocity = mover.rb != null ? mover.rb.velocity : Vector2.zero;
            _tileEffectPositionCaptured = true;
        }

        private static void MovePlayerForTileEffectCheck(Vector2 position)
        {
            if (_tileEffectPlayer == null)
                return;

            _tileEffectPlayer.transform.position = position;
            if (_tileEffectPlayer.itemData?.transform != null)
                _tileEffectPlayer.itemData.transform.position = position;
            if (_tileEffectMover?.rb != null)
            {
                _tileEffectMover.rb.position = position;
                _tileEffectMover.rb.velocity = Vector2.zero;
            }
            Physics2D.SyncTransforms();
        }

        private static void RestoreTileEffectPosition()
        {
            if (!_tileEffectPositionCaptured)
                return;

            MovePlayerForTileEffectCheck(_tileEffectOriginalPosition);
            if (_tileEffectMover?.rb != null)
                _tileEffectMover.rb.velocity = _tileEffectOriginalVelocity;
            _tileEffectReceiver?.RefreshCurrentTileEffects();
            _tileEffectPositionCaptured = false;
        }

        #endregion
    }
}
