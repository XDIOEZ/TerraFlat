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
                    out Vector2 waterPosition, out Vector2 dryPosition,
                    out Vector2 saltWaterPosition, out bool hasSaltWater))
                return;

            Player player = context.Player;
            Mover mover = context.Mover;
            if (player?.itemMods == null || mover?.Speed == null)
                throw new InvalidOperationException("地块效果黄金路径找不到真实玩家或移动速度模块。");

            TileEffectReceiver receiver =
                player.itemMods.GetMod_ByID<TileEffectReceiver>(ModText.TileEffectReceiver) ??
                player.GetComponentInChildren<TileEffectReceiver>(true);
            BuffManager buffManager = player.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
            Mod_InteractSender interactSender = player.GetComponentInChildren<Mod_InteractSender>(true);
            Mod_Food food = player.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
            if (receiver == null || buffManager == null || interactSender == null || food?.Data?.nutrition == null)
                throw new InvalidOperationException("真实玩家缺少 TileEffectReceiver、BuffManager、交互或营养模块。");

            float slowdownFactor = ResolveWaterSlowdownFactor();
            float originalWater = food.Data.nutrition.Water;
            bool hadInfection = buffManager.HasBuff(InfectionBuffIds.Infection);
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
                bool cleanFreshWater = buffManager.HasBuff(FreshWaterBuffIds.Clean);
                bool dirtyFreshWater = buffManager.HasBuff(FreshWaterBuffIds.Dirty);
                if (cleanFreshWater == dirtyFreshWater)
                    throw new InvalidOperationException("淡水必须且只能授予一种水质能力 Buff。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier -
                              drySpeedMultiplier * slowdownFactor) > TileEffectTolerance)
                {
                    throw new InvalidOperationException(
                        $"水体减速倍率异常：陆地={drySpeedMultiplier:0.###}，" +
                        $"水中={mover.Speed.MultiplicativeModifier:0.###}，" +
                        $"配置倍率={slowdownFactor:0.###}。");
                }

                float drinkStartWater = Mathf.Min(originalWater, food.Data.nutrition.Max_Water - 50f);
                food.Data.nutrition.Water = drinkStartWater;
                food.DataUpdate?.Invoke();
                if (!interactSender.BeginFreshWaterDrinkHold())
                    throw new InvalidOperationException("持有淡水能力 Buff 时无法开始长按饮水。");
                interactSender.TickFreshWaterDrinking(0.99f);
                if (Mathf.Abs(food.Data.nutrition.Water - drinkStartWater) > TileEffectTolerance)
                    throw new InvalidOperationException("长按交互键未满1秒时提前补水。");
                interactSender.TickFreshWaterDrinking(0.02f);
                if (Mathf.Abs(food.Data.nutrition.Water -
                              (drinkStartWater + interactSender.FreshWaterGainPerTick)) >
                    TileEffectTolerance)
                {
                    throw new InvalidOperationException("长按交互键满1秒后没有按配置恢复水分。");
                }
                if (!interactSender.LastFreshWaterDrinkAudioHandle.IsValid ||
                    !interactSender.LastFreshWaterDrinkAudioHandle.IsPlaying)
                    throw new InvalidOperationException("淡水饮用 Tick 没有播放 food.drink 音效。");
                if (interactSender.LastFreshWaterDrinkEffect == null ||
                    !interactSender.LastFreshWaterDrinkEffect.activeInHierarchy)
                    throw new InvalidOperationException("淡水饮用 Tick 没有播放蓝色水粒子。");
                if (dirtyFreshWater)
                {
                    interactSender.ProcessFreshWaterDrinkPulse(0f, false);
                    if (!buffManager.HasBuff(InfectionBuffIds.Infection))
                        throw new InvalidOperationException("脏淡水的20%饮水判定没有授予感染 Buff。");
                }
                else if (!hadInfection && buffManager.HasBuff(InfectionBuffIds.Infection))
                {
                    throw new InvalidOperationException("干净淡水饮用错误触发了感染。");
                }
                interactSender.EndFreshWaterDrinkHold();

                if (hasSaltWater)
                {
                    MovePlayerForTileEffectCheck(saltWaterPosition);
                    if (!receiver.RefreshCurrentTileEffects() ||
                        receiver.currentTileData is not TileData_Water)
                        throw new InvalidOperationException("TileEffectReceiver 未识别运行时盐水地块。");
                    if (!buffManager.HasBuff(SaltWaterBuffIds.InSaltWater))
                        throw new InvalidOperationException("玩家进入运行时盐水后未获得位于盐水中 Buff。");
                    if (buffManager.HasBuff(FreshWaterBuffIds.Clean) ||
                        buffManager.HasBuff(FreshWaterBuffIds.Dirty))
                        throw new InvalidOperationException("进入盐水后仍残留淡水饮用能力 Buff。");

                    float saltDrinkStartWater = Mathf.Min(
                        food.Data.nutrition.Water,
                        food.Data.nutrition.Max_Water - interactSender.FreshWaterGainPerTick);
                    food.Data.nutrition.Water = saltDrinkStartWater;
                    food.DataUpdate?.Invoke();
                    if (!interactSender.BeginFreshWaterDrinkHold())
                        throw new InvalidOperationException("持有位于盐水中 Buff 时无法开始长按饮水。");
                    interactSender.TickFreshWaterDrinking(1.01f);
                    if (Mathf.Abs(food.Data.nutrition.Water -
                                  (saltDrinkStartWater + interactSender.FreshWaterGainPerTick)) >
                        TileEffectTolerance)
                        throw new InvalidOperationException("盐水未复用现有长按饮水补水逻辑。");
                    interactSender.EndFreshWaterDrinkHold();
                }

                MovePlayerForTileEffectCheck(dryPosition);
                receiver.RefreshCurrentTileEffects();
                interactSender.TickFreshWaterDrinking(0f);
                if (buffManager.HasBuff(WaterSlowBuffId) || buffManager.HasBuff(WetBuffId) ||
                    buffManager.HasBuff(FreshWaterBuffIds.Clean) ||
                    buffManager.HasBuff(FreshWaterBuffIds.Dirty) ||
                    buffManager.HasBuff(SaltWaterBuffIds.InSaltWater))
                    throw new InvalidOperationException("玩家离开运行时水体后水体 Buff 未移除。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier - drySpeedMultiplier) >
                    TileEffectTolerance)
                    throw new InvalidOperationException("玩家离开水体后移动速度倍率未恢复。");

                _tileEffectScenarioCompleted = true;
                Debug.Log("[GoldenPath][TileEffects] 运行时淡水/盐水进入、饮水及离开恢复验证通过。");
            }
            finally
            {
                interactSender.EndFreshWaterDrinkHold();
                interactSender.LastFreshWaterDrinkAudioHandle.Stop();
                food.Data.nutrition.Water = originalWater;
                food.DataUpdate?.Invoke();
                if (!hadInfection)
                    buffManager.RemoveBuff(InfectionBuffIds.Infection);
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
            out Vector2 waterPosition, out Vector2 dryPosition,
            out Vector2 saltWaterPosition, out bool foundSaltWater)
        {
            waterPosition = default;
            dryPosition = default;
            saltWaterPosition = default;
            bool foundWater = false;
            bool foundDry = false;
            foundSaltWater = false;

            foreach (ChunkRuntime chunk in manager.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk?.Terrain;
                if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
                    chunk.SimulationStatus != ChunkSimulationStatus.Active || terrain == null)
                    continue;

                for (int y = 0; y < terrain.Height && (!foundWater || !foundDry || !foundSaltWater); y++)
                for (int x = 0; x < terrain.Width && (!foundWater || !foundDry || !foundSaltWater); x++)
                {
                    TerrainCell cell = terrain.GetCell(x, y);
                    bool isWater = (cell.Flags & TerrainCellFlags.Water) != 0;
                    bool isDryCandidate = !isWater && terrain.IsWalkable(x, y);
                    if ((!isWater && (foundDry || !isDryCandidate)) ||
                        (isWater && foundWater && foundSaltWater))
                        continue;

                    var localCell = new Vector2Int(x, y);
                    var worldCell = new Vector2Int(
                        chunk.Address.ChunkOrigin.X + x, chunk.Address.ChunkOrigin.Y + y);
                    if (!ChunkRuntimeTileEffectResolver.TryCreateTileEffectData(
                            manager.ActiveGenerationProfile, terrain, localCell, worldCell,
                            out TileData tileData, out _))
                        continue;

                    Vector2 center = worldCell + new Vector2(0.5f, 0.5f);
                    if (isWater && tileData is TileData_Water water && water.salt > 0.01f &&
                        !foundSaltWater)
                    {
                        saltWaterPosition = center;
                        foundSaltWater = true;
                    }
                    else if (isWater && tileData is TileData_Water freshWater &&
                             freshWater.salt <= 0.01f && !foundWater)
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

                if (foundWater && foundDry && foundSaltWater)
                    return true;
            }

            return foundWater && foundDry;
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
