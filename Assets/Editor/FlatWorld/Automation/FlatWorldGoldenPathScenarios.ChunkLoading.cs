using System;

namespace FlatWorld.Automation
{
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const float GoldenChunkLoadSpeedMultiplier = 2.5f;
        private static ChunkMgr goldenChunkManager;
        private static float originalChunkLoadSpeedMultiplier;
        private static bool chunkLoadSpeedScenarioCompleted;

        private static void ResetChunkLoadSpeedScenario()
        {
            goldenChunkManager = null;
            originalChunkLoadSpeedMultiplier = 1f;
            chunkLoadSpeedScenarioCompleted = false;
        }

        private static void RunChunkLoadSpeedScenario(FlatWorldGoldenPathScenarioContext context)
        {
            goldenChunkManager = ChunkMgr.Instance;
            if (goldenChunkManager == null)
                throw new InvalidOperationException("Golden Path 找不到 ChunkMgr，无法验证区块加载倍率。 ");

            originalChunkLoadSpeedMultiplier = goldenChunkManager.ChunkLoadSpeedMultiplier;
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
                    goldenChunkManager.EffectiveMaxConcurrentChunkLoads < 2)
                {
                    throw new InvalidOperationException(
                        "区块加载倍率没有提升实际队列预算或并发上限。 ");
                }

                chunkLoadSpeedScenarioCompleted = true;
            }
            finally
            {
                goldenChunkManager.TrySetChunkLoadSpeedMultiplier(
                    originalChunkLoadSpeedMultiplier,
                    out _);
            }
        }

        private static void AssertChunkLoadSpeedScenarioCompleted()
        {
            if (!chunkLoadSpeedScenarioCompleted)
                throw new InvalidOperationException("Golden Path 未完成 GM 区块加载倍率场景。 ");
        }

        private static void CleanupChunkLoadSpeedScenario()
        {
            if (goldenChunkManager != null)
            {
                goldenChunkManager.TrySetChunkLoadSpeedMultiplier(
                    originalChunkLoadSpeedMultiplier,
                    out _);
            }

            goldenChunkManager = null;
        }
    }
}
