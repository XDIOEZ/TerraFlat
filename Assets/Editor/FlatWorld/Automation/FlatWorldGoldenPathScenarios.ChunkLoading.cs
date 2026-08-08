using System;

namespace FlatWorld.Automation
{
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const float GoldenChunkLoadSpeedMultiplier = 2.5f;
        private static ChunkMgr goldenChunkManager;
        private static float originalChunkLoadSpeedMultiplier;
        private static bool originalChunkLoadSpeedUnlimited;
        private static bool chunkLoadSpeedScenarioCompleted;

        private static void ResetChunkLoadSpeedScenario()
        {
            goldenChunkManager = null;
            originalChunkLoadSpeedMultiplier = 1f;
            originalChunkLoadSpeedUnlimited = false;
            chunkLoadSpeedScenarioCompleted = false;
        }

        private static void RunChunkLoadSpeedScenario(FlatWorldGoldenPathScenarioContext context)
        {
            goldenChunkManager = ChunkMgr.Instance;
            if (goldenChunkManager == null)
                throw new InvalidOperationException("Golden Path 找不到 ChunkMgr，无法验证区块加载倍率。 ");

            originalChunkLoadSpeedUnlimited = goldenChunkManager.IsChunkLoadSpeedUnlimited;
            originalChunkLoadSpeedMultiplier = originalChunkLoadSpeedUnlimited
                ? 1f
                : goldenChunkManager.ChunkLoadSpeedMultiplier;
            try
            {
                if (!goldenChunkManager.TrySetChunkLoadSpeedMultiplier(
                        GoldenChunkLoadSpeedMultiplier,
                        out float appliedMultiplier))
                {
                    throw new InvalidOperationException("ChunkMgr 拒绝了有效的区块加载倍率。 ");
                }

                if (Math.Abs(appliedMultiplier - GoldenChunkLoadSpeedMultiplier) > 0.001f ||
                    Math.Abs(goldenChunkManager.ChunkLoadSpeedMultiplier - appliedMultiplier) > 0.001f)
                {
                    throw new InvalidOperationException(
                        $"区块加载倍率未正确应用：requested={GoldenChunkLoadSpeedMultiplier}, " +
                        $"applied={appliedMultiplier}, current={goldenChunkManager.ChunkLoadSpeedMultiplier}");
                }

                if (goldenChunkManager.EffectiveMaxChunkLoadPerFrame < 2 ||
                    goldenChunkManager.EffectiveMaxConcurrentChunkLoads < 2 ||
                    goldenChunkManager.RuntimeChunks.MaxGenerationConcurrency !=
                    goldenChunkManager.EffectiveBackgroundGenerationConcurrency)
                {
                    throw new InvalidOperationException(
                        "区块加载倍率没有同步提升旧队列或新 WorldModel 生成并发。 ");
                }

                if (!goldenChunkManager.TrySetChunkLoadSpeedMultiplier(
                        float.PositiveInfinity,
                        out appliedMultiplier) ||
                    !goldenChunkManager.IsChunkLoadSpeedUnlimited ||
                    !float.IsPositiveInfinity(appliedMultiplier) ||
                    goldenChunkManager.EffectiveMaxChunkLoadPerFrame != 4 ||
                    goldenChunkManager.EffectiveMaxConcurrentChunkLoads !=
                    ChunkMgr.SafeBackgroundGenerationCeiling ||
                    goldenChunkManager.EffectiveBackgroundGenerationConcurrency !=
                    ChunkMgr.SafeBackgroundGenerationCeiling ||
                    goldenChunkManager.RuntimeChunks.MaxGenerationConcurrency !=
                    ChunkMgr.SafeBackgroundGenerationCeiling ||
                    ChunkMgr.ScaleCurrentChunkLoadItemBudget(256, 1) != 1024 ||
                    Math.Abs(ChunkMgr.ScaleCurrentChunkLoadFrameBudget(1.5f, 0.25f) - 3f) > 0.001f)
                {
                    throw new InvalidOperationException(
                        "区块加载自动最大状态没有应用 CPU 与主线程安全上限。 ");
                }

                chunkLoadSpeedScenarioCompleted = true;
            }
            finally
            {
                RestoreOriginalChunkLoadSpeed();
            }
        }

        private static void AssertChunkLoadSpeedScenarioCompleted()
        {
            if (!chunkLoadSpeedScenarioCompleted)
                throw new InvalidOperationException("Golden Path 未完成 GM 区块加载倍率场景。 ");
        }

        private static void CleanupChunkLoadSpeedScenario()
        {
            RestoreOriginalChunkLoadSpeed();
            goldenChunkManager = null;
        }

        private static void RestoreOriginalChunkLoadSpeed()
        {
            if (goldenChunkManager == null)
                return;

            goldenChunkManager.TrySetChunkLoadSpeedMultiplier(
                originalChunkLoadSpeedUnlimited
                    ? float.PositiveInfinity
                    : originalChunkLoadSpeedMultiplier,
                out _);
        }
    }
}
