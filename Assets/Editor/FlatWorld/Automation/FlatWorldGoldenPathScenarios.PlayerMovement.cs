using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>通过真实玩家 API 验证出生、奔跑输入与管理员移动速度倍率。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 新玩家安全出生点

        private static bool _initialSpawnLandScenarioCompleted;

        private static void ResetInitialSpawnLandScenario()
        {
            _initialSpawnLandScenarioCompleted = false;
        }

        /// <summary>确认新玩家首次进入世界后，运行时位置与存档位置都落在同一块可走陆地。</summary>
        private static void VerifyInitialPlayerSpawnLand(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player?.Data == null)
                throw new InvalidOperationException("出生点场景找不到真实玩家数据。");

            ChunkMgr chunkManager = ChunkMgr.Instance;
            Vector2 runtimePosition = context.Player.transform.position;
            if (chunkManager == null ||
                !chunkManager.TryGetRuntimeTerrainTile(
                    runtimePosition, out RuntimeTerrainTileSample sample))
            {
                throw new InvalidOperationException(
                    $"出生点场景无法读取玩家脚下的权威地形：position={runtimePosition}。");
            }

            bool isWater = (sample.Cell.Flags &
                            FlatWorld.WorldModel.TerrainCellFlags.Water) != 0;
            bool isWalkable = sample.Terrain.IsWalkable(
                sample.LocalCell.x, sample.LocalCell.y);
            if (isWater || !isWalkable)
            {
                throw new InvalidOperationException(
                    $"新玩家出生格不是安全陆地：cell={sample.WorldCell}, " +
                    $"water={isWater}, walkable={isWalkable}。");
            }

            Vector2 savedPosition = context.Player.Data.transform.position;
            if (WorldTopologyRuntime.Distance(runtimePosition, savedPosition) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"新玩家运行时位置与存档位置不一致：runtime={runtimePosition}, " +
                    $"saved={savedPosition}。");
            }

            _initialSpawnLandScenarioCompleted = true;
            Debug.Log($"[GoldenPath][Player] 新玩家安全陆地出生验证通过：{sample.WorldCell}。");
        }

        private static void AssertInitialSpawnLandScenarioCompleted()
        {
            if (!_initialSpawnLandScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未验证新玩家安全陆地出生。");
        }

        private static void CleanupInitialSpawnLandScenario()
        {
            _initialSpawnLandScenarioCompleted = false;
        }

        #endregion

        #region 奔跑输入状态

        private const float RunInputSpeedTolerance = 0.001f;

        private static Mover _runInputMover;
        private static float _runInputOriginalModifier;
        private static bool _runInputScenarioCompleted;

        private static void ResetPlayerRunInputScenario()
        {
            _runInputMover = null;
            _runInputOriginalModifier = 1f;
            _runInputScenarioCompleted = false;
        }

        /// <summary>验证按住奔跑并在松开后恢复普通移动。</summary>
        private static void RunPlayerRunInputScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            _runInputMover = context.Mover;
            if (_runInputMover?.Speed == null)
                throw new InvalidOperationException("奔跑输入场景找不到真实玩家 Mover。");
            if (_runInputMover.IsRunning)
                throw new InvalidOperationException("奔跑输入场景开始前玩家已处于奔跑状态。");

            _runInputOriginalModifier = _runInputMover.Speed.MultiplicativeModifier;
            _runInputMover.HandleRunInputPressed();

            float expectedRunModifier = _runInputOriginalModifier * _runInputMover.RunSpeedRate;
            if (!_runInputMover.IsRunning ||
                Mathf.Abs(_runInputMover.Speed.MultiplicativeModifier - expectedRunModifier) >
                RunInputSpeedTolerance)
            {
                throw new InvalidOperationException(
                    "按下奔跑输入后未进入奔跑状态或速度倍率异常。");
            }

            _runInputMover.HandleRunInputReleased(1d);
            if (_runInputMover.IsRunning ||
                Mathf.Abs(_runInputMover.Speed.MultiplicativeModifier - _runInputOriginalModifier) >
                RunInputSpeedTolerance)
            {
                throw new InvalidOperationException("松开奔跑输入后未恢复普通移动。");
            }

            _runInputScenarioCompleted = true;
            Debug.Log("[GoldenPath][Player] 按住奔跑、松开恢复普通移动验证通过。");
        }

        private static void AssertPlayerRunInputScenarioCompleted()
        {
            if (!_runInputScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成奔跑输入验证。");
        }

        private static void CleanupPlayerRunInputScenario()
        {
            if (_runInputMover != null && _runInputMover.IsRunning)
                _runInputMover.SetRunState(false);
            _runInputMover = null;
        }

        #endregion

        #region 管理员移动速度

        private const float GoldenPathMoveSpeedMultiplier = 2.75f;
        private const float MoveSpeedTolerance = 0.001f;

        private static PlayerAdminController _moveSpeedAdminController;
        private static float _moveSpeedOriginalAdminMultiplier;
        private static float _moveSpeedOriginalModifier;
        private static bool _moveSpeedScenarioApplied;
        private static bool _moveSpeedScenarioCompleted;

        private static void ResetPlayerMoveSpeedScenario()
        {
            _moveSpeedAdminController = null;
            _moveSpeedOriginalAdminMultiplier = 1f;
            _moveSpeedOriginalModifier = 1f;
            _moveSpeedScenarioApplied = false;
            _moveSpeedScenarioCompleted = false;
        }

        private static void RunPlayerMoveSpeedScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || context.Mover?.Speed == null)
                throw new InvalidOperationException("移动速度倍率场景找不到真实玩家 Mover。");

            _moveSpeedAdminController =
                context.Player.GetComponentInChildren<PlayerAdminController>(true);
            if (_moveSpeedAdminController == null)
                throw new InvalidOperationException("真实玩家缺少 PlayerAdminController。");

            _moveSpeedOriginalAdminMultiplier =
                _moveSpeedAdminController.AdminMoveSpeedMultiplier;
            _moveSpeedOriginalModifier = context.Mover.Speed.MultiplicativeModifier;
            if (!_moveSpeedAdminController.TrySetAdminMoveSpeedMultiplier(
                    GoldenPathMoveSpeedMultiplier,
                    out float appliedMultiplier))
            {
                throw new InvalidOperationException("真实玩家拒绝了合法的移动速度倍率。");
            }

            _moveSpeedScenarioApplied = true;
            float expectedModifier = _moveSpeedOriginalModifier /
                                     Mathf.Max(0.01f, _moveSpeedOriginalAdminMultiplier) *
                                     GoldenPathMoveSpeedMultiplier;
            if (Mathf.Abs(appliedMultiplier - GoldenPathMoveSpeedMultiplier) > MoveSpeedTolerance ||
                Mathf.Abs(context.Mover.Speed.MultiplicativeModifier - expectedModifier) >
                MoveSpeedTolerance)
            {
                throw new InvalidOperationException(
                    $"管理员移速倍率未正确应用：applied={appliedMultiplier}, " +
                    $"modifier={context.Mover.Speed.MultiplicativeModifier}, expected={expectedModifier}。");
            }

            RestorePlayerMoveSpeedScenario();
            if (Mathf.Abs(context.Mover.Speed.MultiplicativeModifier - _moveSpeedOriginalModifier) >
                MoveSpeedTolerance)
            {
                throw new InvalidOperationException("管理员移速倍率恢复后破坏了原有速度修饰值。");
            }

            _moveSpeedScenarioCompleted = true;
            Debug.Log(
                $"[GoldenPath][Player] 自定义移速倍率 {GoldenPathMoveSpeedMultiplier:0.##}x " +
                "应用与恢复均通过。");
        }

        private static void AssertPlayerMoveSpeedScenarioCompleted()
        {
            if (!_moveSpeedScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成自定义移速倍率验证。");
        }

        private static void CleanupPlayerMoveSpeedScenario()
        {
            RestorePlayerMoveSpeedScenario();
            _moveSpeedAdminController = null;
        }

        private static void RestorePlayerMoveSpeedScenario()
        {
            if (!_moveSpeedScenarioApplied || _moveSpeedAdminController == null)
                return;

            if (!_moveSpeedAdminController.TrySetAdminMoveSpeedMultiplier(
                    _moveSpeedOriginalAdminMultiplier,
                    out _))
            {
                throw new InvalidOperationException("清理自定义移速倍率时无法恢复原倍率。");
            }

            _moveSpeedScenarioApplied = false;
        }

        #endregion
    }
}
