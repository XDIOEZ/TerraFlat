using System;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实世界区块上验证玩家进出水体时的环境动作与配置型 Buff 生命周期。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
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

            float originalWater = food.Data.nutrition.Water;
            bool hadInfection = buffManager.HasBuff(InfectionBuffIds.Infection);
            DrinkWaterActionInstance lastDrinkAction = null;
            CaptureTileEffectPosition(player, mover, receiver);
            try
            {
                MovePlayerForTileEffectCheck(dryPosition);
                if (!receiver.RefreshCurrentTileEffects())
                    throw new InvalidOperationException("TileEffectReceiver 无法绑定运行时陆地地块。");
                if (buffManager.HasBuff(WetBuffId) ||
                    receiver.EnvironmentInteractions.ActiveEffectCount != 0)
                    throw new InvalidOperationException("进入水体前玩家已残留水体 Buff。");

                float drySpeedMultiplier = mover.Speed.MultiplicativeModifier;
                MovePlayerForTileEffectCheck(waterPosition);
                if (!receiver.RefreshCurrentTileEffects() ||
                    receiver.currentTileData is not TileData_Water)
                    throw new InvalidOperationException("TileEffectReceiver 未识别运行时水体地块。");
                if (!buffManager.HasBuff(WetBuffId))
                    throw new InvalidOperationException("玩家进入运行时水体后未获得潮湿 Buff。");
                if (!receiver.EnvironmentInteractions.TryGetEffectDefinition(
                        out MoveSpeedEnvironmentEffectDefinition slowdownDefinition) ||
                    receiver.EnvironmentInteractions.ActiveEffectCount != 1)
                {
                    throw new InvalidOperationException("玩家进入水体后未获得独立的环境减速实例。");
                }
                if (!receiver.EnvironmentInteractions.TryGetDefinition(
                        out DrinkWaterActionDefinition freshWaterDefinition) ||
                    freshWaterDefinition.WaterKind == WaterEnvironmentKind.Salt)
                {
                    throw new InvalidOperationException("淡水未提供有效的淡水饮用动作定义。");
                }
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier -
                              drySpeedMultiplier * slowdownDefinition.Multiplier) >
                    TileEffectTolerance)
                {
                    throw new InvalidOperationException(
                        $"水体减速倍率异常：陆地={drySpeedMultiplier:0.###}，" +
                        $"水中={mover.Speed.MultiplicativeModifier:0.###}，" +
                        $"配置倍率={slowdownDefinition.Multiplier:0.###}。");
                }

                buffManager.RemoveBuff(WetBuffId);
                if (buffManager.HasBuff(WetBuffId))
                    throw new InvalidOperationException("潮湿 Buff 无法通过标准清理入口移除。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier -
                              drySpeedMultiplier * slowdownDefinition.Multiplier) >
                    TileEffectTolerance)
                {
                    throw new InvalidOperationException("清除潮湿 Buff 错误解除了水体环境减速。");
                }

                float drinkStartWater = Mathf.Min(originalWater, food.Data.nutrition.Max_Water - 50f);
                food.Data.nutrition.Water = drinkStartWater;
                food.DataUpdate?.Invoke();
                if (!interactSender.BeginEnvironmentActionHold())
                    throw new InvalidOperationException("淡水环境无法开始长按饮水动作。");
                lastDrinkAction = receiver.EnvironmentInteractions.ActiveAction as DrinkWaterActionInstance;
                if (lastDrinkAction == null)
                    throw new InvalidOperationException("淡水环境没有创建角色独享的喝水动作实例。");
                interactSender.TickEnvironmentInteraction(0.99f);
                if (Mathf.Abs(food.Data.nutrition.Water - drinkStartWater) > TileEffectTolerance)
                    throw new InvalidOperationException("长按交互键未满1秒时提前补水。");
                interactSender.TickEnvironmentInteraction(0.02f);
                if (Mathf.Abs(food.Data.nutrition.Water -
                              (drinkStartWater + freshWaterDefinition.WaterGainPerTick)) >
                    TileEffectTolerance)
                {
                    throw new InvalidOperationException("长按交互键满1秒后没有按配置恢复水分。");
                }
                if (!lastDrinkAction.LastAudioHandle.IsValid ||
                    !lastDrinkAction.LastAudioHandle.IsPlaying)
                    throw new InvalidOperationException("淡水饮用 Tick 没有播放 food.drink 音效。");
                if (lastDrinkAction.LastEffect == null ||
                    !lastDrinkAction.LastEffect.activeInHierarchy)
                    throw new InvalidOperationException("淡水饮用 Tick 没有播放蓝色水粒子。");
                if (freshWaterDefinition.WaterKind == WaterEnvironmentKind.DirtyFresh)
                {
                    lastDrinkAction.ProcessPulse(0f, false);
                    if (!buffManager.HasBuff(InfectionBuffIds.Infection))
                        throw new InvalidOperationException("脏淡水的20%饮水判定没有授予感染 Buff。");
                }
                else if (!hadInfection && buffManager.HasBuff(InfectionBuffIds.Infection))
                {
                    throw new InvalidOperationException("干净淡水饮用错误触发了感染。");
                }
                interactSender.EndEnvironmentActionHold();

                if (hasSaltWater)
                {
                    MovePlayerForTileEffectCheck(saltWaterPosition);
                    if (!receiver.RefreshCurrentTileEffects() ||
                        receiver.currentTileData is not TileData_Water)
                        throw new InvalidOperationException("TileEffectReceiver 未识别运行时盐水地块。");
                    if (!receiver.EnvironmentInteractions.TryGetDefinition(
                            out DrinkWaterActionDefinition saltWaterDefinition) ||
                        saltWaterDefinition.WaterKind != WaterEnvironmentKind.Salt)
                    {
                        throw new InvalidOperationException("盐水未提供盐水饮用动作定义。");
                    }

                    float saltDrinkStartWater = Mathf.Min(
                        food.Data.nutrition.Water,
                        food.Data.nutrition.Max_Water - saltWaterDefinition.WaterGainPerTick);
                    food.Data.nutrition.Water = saltDrinkStartWater;
                    food.DataUpdate?.Invoke();
                    if (!interactSender.BeginEnvironmentActionHold())
                        throw new InvalidOperationException("盐水环境无法开始长按饮水动作。");
                    lastDrinkAction = receiver.EnvironmentInteractions.ActiveAction as DrinkWaterActionInstance;
                    if (lastDrinkAction == null)
                        throw new InvalidOperationException("盐水环境没有创建角色独享的喝水动作实例。");
                    interactSender.TickEnvironmentInteraction(1.01f);
                    if (Mathf.Abs(food.Data.nutrition.Water -
                                  (saltDrinkStartWater + saltWaterDefinition.WaterGainPerTick)) >
                        TileEffectTolerance)
                        throw new InvalidOperationException("盐水未复用现有长按饮水补水逻辑。");
                    interactSender.EndEnvironmentActionHold();
                }

                MovePlayerForTileEffectCheck(dryPosition);
                receiver.RefreshCurrentTileEffects();
                interactSender.TickEnvironmentInteraction(0f);
                if (buffManager.HasBuff(WetBuffId) ||
                    receiver.EnvironmentInteractions.AvailableActionCount != 0 ||
                    receiver.EnvironmentInteractions.ActiveAction != null ||
                    receiver.EnvironmentInteractions.ActiveEffectCount != 0)
                    throw new InvalidOperationException("玩家离开运行时水体后环境动作或水体 Buff 未清理。");
                if (Mathf.Abs(mover.Speed.MultiplicativeModifier - drySpeedMultiplier) >
                    TileEffectTolerance)
                    throw new InvalidOperationException("玩家离开水体后移动速度倍率未恢复。");

                _tileEffectScenarioCompleted = true;
                Debug.Log("[GoldenPath][TileEffects] 运行时淡水/盐水进入、饮水及离开恢复验证通过。");
            }
            finally
            {
                interactSender.EndEnvironmentActionHold();
                lastDrinkAction?.LastAudioHandle.Stop();
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
