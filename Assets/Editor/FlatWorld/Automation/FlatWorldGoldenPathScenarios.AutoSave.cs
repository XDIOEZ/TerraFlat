using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 自动保存回归场景：使用正式分帧入口创建后台写盘任务，
    /// 并在移动阶段确认保存没有锁住玩家输入、Mover 或全局时间。
    /// </summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static Task<bool> autoSaveWriteTask;
        private static GameController autoSaveGameController;
        private static bool autoSaveSnapshotPending;
        private static bool manualSaveRequested;
        private static bool autoSaveScenarioCompleted;
        private static double autoSaveDeadline;
        private static float autoSaveInitialTimeScale;

        private static void ResetAutoSaveScenario()
        {
            autoSaveWriteTask = null;
            autoSaveGameController = null;
            autoSaveSnapshotPending = false;
            manualSaveRequested = false;
            autoSaveScenarioCompleted = false;
            autoSaveDeadline = 0d;
            autoSaveInitialTimeScale = 1f;
        }

        private static void BeginAutoSaveScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.GameManager == null || context.Player == null || context.Mover == null)
                throw new InvalidOperationException("AutoSave: 世界就绪后缺少游戏管理器、玩家或 Mover。");

            autoSaveGameController = context.Player.GetComponentInChildren<GameController>(true);
            if (autoSaveGameController == null)
                throw new InvalidOperationException("AutoSave: 玩家缺少 GameController。");

            autoSaveInitialTimeScale = Time.timeScale;
            autoSaveSnapshotPending = true;
            autoSaveDeadline = Time.realtimeSinceStartupAsDouble + 10d;
            context.GameManager.StartCoroutine(
                context.GameManager.SaveGameInBackgroundCoroutine(task =>
                {
                    autoSaveWriteTask = task;
                    autoSaveSnapshotPending = false;
                }));
        }

        private static void TickAutoSaveScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (autoSaveScenarioCompleted)
                return;

            if (autoSaveSnapshotPending || autoSaveWriteTask == null || !autoSaveWriteTask.IsCompleted)
            {
                if (Time.realtimeSinceStartupAsDouble > autoSaveDeadline)
                    throw new InvalidOperationException("AutoSave: 分帧快照或后台写盘超过 10 秒未完成。");
                return;
            }

            bool wroteToDisk;
            try
            {
                wroteToDisk = autoSaveWriteTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("AutoSave: 后台写盘任务失败。", exception);
            }

            if (!wroteToDisk)
                throw new InvalidOperationException("AutoSave: 当前世界没有生成有效的自动保存文件。");
            if (autoSaveGameController == null || autoSaveGameController.IsGameplayInputLocked)
                throw new InvalidOperationException("AutoSave: 保存后玩家输入仍被锁定。");
            if (context.Mover == null || !context.Mover.enabled || context.Mover.rb == null)
                throw new InvalidOperationException("AutoSave: 保存后玩家移动模块或 Rigidbody2D 不可用。");
            if (!Mathf.Approximately(Time.timeScale, autoSaveInitialTimeScale))
            {
                throw new InvalidOperationException(
                    $"AutoSave: 保存改变了全局时间缩放：{autoSaveInitialTimeScale} -> {Time.timeScale}。");
            }

            // 同一场景继续验证玩家点击“保存游戏”走的是分帧快照与后台写盘路径。
            if (!manualSaveRequested)
            {
                manualSaveRequested = true;
                context.GameManager.SaveGame();
                autoSaveDeadline = Time.realtimeSinceStartupAsDouble + 10d;
                return;
            }

            if (context.GameManager.IsSaveInProgress)
            {
                if (Time.realtimeSinceStartupAsDouble > autoSaveDeadline)
                    throw new InvalidOperationException("AutoSave: 手动保存超过 10 秒未完成。");
                return;
            }

            if (context.GameManager.LastSaveSucceeded != true)
                throw new InvalidOperationException("AutoSave: 手动保存未成功完成。");

            autoSaveScenarioCompleted = true;
        }

        private static void AssertAutoSaveScenarioCompleted()
        {
            if (!autoSaveScenarioCompleted)
                throw new InvalidOperationException("AutoSave: 自动保存、输入与移动回归场景未完成。");
        }

        private static void CleanupAutoSaveScenario()
        {
            autoSaveWriteTask = null;
            autoSaveGameController = null;
            autoSaveSnapshotPending = false;
            manualSaveRequested = false;
        }
    }
}
