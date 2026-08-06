using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 黄金路径玩法场景可读上下文。不持有跨 Domain Reload 的 Unity 对象。
    /// </summary>
    internal readonly struct FlatWorldGoldenPathScenarioContext
    {
        internal GameManager GameManager { get; }
        internal SaveDataMgr SaveDataManager { get; }
        internal Player Player { get; }
        internal Mover Mover { get; }
        internal int WaypointIndex { get; }
        internal int WaypointCount { get; }
        internal Vector2 CurrentPosition { get; }
        internal Vector2 TargetPosition { get; }
        internal Vector2Int ExpectedChunk { get; }
        internal FlatWorldGoldenPathConfiguration Configuration { get; }

        internal FlatWorldGoldenPathScenarioContext(
            GameManager gameManager,
            SaveDataMgr saveDataManager,
            Player player,
            Mover mover,
            int waypointIndex,
            int waypointCount,
            Vector2 currentPosition,
            Vector2 targetPosition,
            Vector2Int expectedChunk,
            FlatWorldGoldenPathConfiguration configuration)
        {
            GameManager = gameManager;
            SaveDataManager = saveDataManager;
            Player = player;
            Mover = mover;
            WaypointIndex = waypointIndex;
            WaypointCount = waypointCount;
            CurrentPosition = currentPosition;
            TargetPosition = targetPosition;
            ExpectedChunk = expectedChunk;
            Configuration = configuration;
        }
    }

    /// <summary>
    /// 真实黄金路径的稳定扩展点。阶段方法只做编排，
    /// 具体玩法可拆到 FlatWorldGoldenPathScenarios.&lt;Subsystem&gt;.cs。
    /// 调用顺序：Reset → OnWorldReady → OnTraversalTick/OnChunkReady → BeforeWorldExit → Cleanup。
    /// </summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        internal static void Reset()
        {
            // 在新的黄金路径开始前重置各玩法场景的静态状态。
            ResetBurningBuffScenario();
            ResetHydrologyScenario();
            ResetWorldWrapScenario();
            ResetWorldModelScenario();
            ResetPlayerMoveSpeedScenario();
            ResetChunkLoadSpeedScenario();
        }

        internal static void OnWorldReady(FlatWorldGoldenPathScenarioContext context)
        {
            // 玩家和初始 Chunk 就绪后的一次性安排挂在这里。
            if (context.Configuration.scenarios.hydrology)
                BeginHydrologyScenario(context);
            BeginWorldModelScenario(context);
            RunPlayerMoveSpeedScenario(context);
            RunChunkLoadSpeedScenario(context);
        }

        internal static void OnTraversalTick(FlatWorldGoldenPathScenarioContext context)
        {
            // 与移动并行、需要跨 Tick 观测的场景挂在这里。
            // 该回调会重复执行，子场景必须幂等且不得阻塞。
            if (context.Configuration.scenarios.burningBuff)
                TickBurningBuffScenario(context);
        }

        internal static void OnChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            // 每个目标 Chunk 就绪后的阶段断言挂在这里。
            if (context.Configuration.scenarios.burningBuff)
                VerifyBurningBuffAtChunkReady(context);
            if (context.Configuration.scenarios.hydrology)
                VerifyHydrologyAtChunkReady(context);
        }

        internal static void BeforeWorldExit(FlatWorldGoldenPathScenarioContext context)
        {
            // 完成移动后、退出世界前的长时状态断言挂在这里。
            if (context.Configuration.scenarios.burningBuff)
                AssertBurningBuffScenarioCompleted();
            if (context.Configuration.scenarios.hydrology)
                AssertHydrologyScenarioCompleted();
            if (context.Configuration.scenarios.worldWrap)
                AssertWorldWrapScenarioCompleted();
            AssertWorldModelScenarioCompleted();
            AssertPlayerMoveSpeedScenarioCompleted();
            AssertChunkLoadSpeedScenarioCompleted();
        }

        internal static void Cleanup(FlatWorldGoldenPathScenarioContext context)
        {
            // 通过和失败都会调用；在这里恢复 Buff、生命、物品和临时对象。
            CleanupBurningBuffScenario();
            CleanupHydrologyScenario();
            CleanupWorldWrapScenario();
            CleanupWorldModelScenario();
            CleanupPlayerMoveSpeedScenario();
            CleanupChunkLoadSpeedScenario();
        }
    }
}
