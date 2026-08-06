using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>通过真实本地玩家管理员 API 验证自定义移动速度倍率。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
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
    }
}
