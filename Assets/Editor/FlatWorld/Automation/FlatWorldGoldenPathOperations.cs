using System;
using System.Collections.Generic;
using System.Linq;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 黄金路径可配置操作契约。每个操作必须提供稳定 ID，并负责自身状态重置、断言与清理；
    /// 默认完整配置执行全部操作，局部 JSON 可按 ID 选择系统相关操作而无需修改主编排代码。
    /// </summary>
    internal interface IFlatWorldGoldenPathOperation
    {
        string Id { get; }
        string SystemId { get; }
        void Reset();
        void OnWorldReady(FlatWorldGoldenPathScenarioContext context);
        bool TickWorldReady(FlatWorldGoldenPathScenarioContext context);
        void OnTraversalTick(FlatWorldGoldenPathScenarioContext context);
        void OnChunkReady(FlatWorldGoldenPathScenarioContext context);
        void BeforeWorldExit(FlatWorldGoldenPathScenarioContext context);
        void Cleanup(FlatWorldGoldenPathScenarioContext context);
    }

    /// <summary>使用委托把现有状态化 partial 场景接入统一操作契约。</summary>
    internal sealed class FlatWorldGoldenPathOperation : IFlatWorldGoldenPathOperation
    {
        private readonly Action reset;
        private readonly Action<FlatWorldGoldenPathScenarioContext> onWorldReady;
        private readonly Func<FlatWorldGoldenPathScenarioContext, bool> tickWorldReady;
        private readonly Action<FlatWorldGoldenPathScenarioContext> onTraversalTick;
        private readonly Action<FlatWorldGoldenPathScenarioContext> onChunkReady;
        private readonly Action<FlatWorldGoldenPathScenarioContext> beforeWorldExit;
        private readonly Action<FlatWorldGoldenPathScenarioContext> cleanup;

        internal string Id { get; }
        internal string SystemId { get; }
        string IFlatWorldGoldenPathOperation.Id => Id;
        string IFlatWorldGoldenPathOperation.SystemId => SystemId;

        internal FlatWorldGoldenPathOperation(
            string id,
            string systemId,
            Action reset = null,
            Action<FlatWorldGoldenPathScenarioContext> onWorldReady = null,
            Func<FlatWorldGoldenPathScenarioContext, bool> tickWorldReady = null,
            Action<FlatWorldGoldenPathScenarioContext> onTraversalTick = null,
            Action<FlatWorldGoldenPathScenarioContext> onChunkReady = null,
            Action<FlatWorldGoldenPathScenarioContext> beforeWorldExit = null,
            Action<FlatWorldGoldenPathScenarioContext> cleanup = null)
        {
            Id = id;
            SystemId = systemId;
            this.reset = reset;
            this.onWorldReady = onWorldReady;
            this.tickWorldReady = tickWorldReady;
            this.onTraversalTick = onTraversalTick;
            this.onChunkReady = onChunkReady;
            this.beforeWorldExit = beforeWorldExit;
            this.cleanup = cleanup;
        }

        public void Reset() => reset?.Invoke();

        public void OnWorldReady(FlatWorldGoldenPathScenarioContext context) =>
            onWorldReady?.Invoke(context);

        public bool TickWorldReady(FlatWorldGoldenPathScenarioContext context) =>
            tickWorldReady?.Invoke(context) ?? true;

        public void OnTraversalTick(FlatWorldGoldenPathScenarioContext context) =>
            onTraversalTick?.Invoke(context);

        public void OnChunkReady(FlatWorldGoldenPathScenarioContext context) =>
            onChunkReady?.Invoke(context);

        public void BeforeWorldExit(FlatWorldGoldenPathScenarioContext context) =>
            beforeWorldExit?.Invoke(context);

        public void Cleanup(FlatWorldGoldenPathScenarioContext context) => cleanup?.Invoke(context);
    }

    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 操作标识

        internal const string WorldWrapOperationId = "world.wrap";
        internal const string WorldModelOperationId = "world.model-streaming";

        #endregion

        #region 操作注册

        private static readonly IReadOnlyList<IFlatWorldGoldenPathOperation> Operations =
            ValidateOperations(CreateOperations());
        private static readonly HashSet<string> FaultedOperationIds =
            new(StringComparer.OrdinalIgnoreCase);

        private static IReadOnlyList<IFlatWorldGoldenPathOperation> CreateOperations()
        {
            return new IFlatWorldGoldenPathOperation[]
            {
                new FlatWorldGoldenPathOperation(
                    "player.spawn-land", "player",
                    reset: ResetInitialSpawnLandScenario,
                    onWorldReady: VerifyInitialPlayerSpawnLand,
                    beforeWorldExit: _ => AssertInitialSpawnLandScenarioCompleted(),
                    cleanup: _ => CleanupInitialSpawnLandScenario()),
                new FlatWorldGoldenPathOperation(
                    "player.interaction-retry", "player",
                    reset: ResetPlayerInteractionRetryScenario,
                    onWorldReady: RunPlayerInteractionRetryScenario,
                    beforeWorldExit: _ => AssertPlayerInteractionRetryScenarioCompleted(),
                    cleanup: _ => CleanupPlayerInteractionRetryScenario()),
                new FlatWorldGoldenPathOperation(
                    "player.run-transition", "player",
                    reset: ResetPlayerRunInputScenario,
                    onWorldReady: RunPlayerRunInputScenario,
                    beforeWorldExit: _ => AssertPlayerRunInputScenarioCompleted(),
                    cleanup: _ => CleanupPlayerRunInputScenario()),
                new FlatWorldGoldenPathOperation(
                    "map.hydrology", "map",
                    reset: ResetHydrologyScenario,
                    onWorldReady: BeginHydrologyScenario,
                    onChunkReady: VerifyHydrologyAtChunkReady,
                    beforeWorldExit: _ => AssertHydrologyScenarioCompleted(),
                    cleanup: _ => CleanupHydrologyScenario()),
                new FlatWorldGoldenPathOperation(
                    WorldModelOperationId, "world-model",
                    reset: ResetWorldModelScenario,
                    onWorldReady: BeginWorldModelScenario,
                    beforeWorldExit: _ => AssertWorldModelScenarioCompleted(),
                    cleanup: _ => CleanupWorldModelScenario()),
                new FlatWorldGoldenPathOperation(
                    "player.admin-move-speed", "player",
                    reset: ResetPlayerMoveSpeedScenario,
                    onWorldReady: RunPlayerMoveSpeedScenario,
                    beforeWorldExit: _ => AssertPlayerMoveSpeedScenarioCompleted(),
                    cleanup: _ => CleanupPlayerMoveSpeedScenario()),
                new FlatWorldGoldenPathOperation(
                    "player.admin-invincibility", "player",
                    reset: ResetPlayerAdminInvincibilityScenario,
                    onWorldReady: RunPlayerAdminInvincibilityScenario,
                    beforeWorldExit: _ => AssertPlayerAdminInvincibilityScenarioCompleted(),
                    cleanup: _ => CleanupPlayerAdminInvincibilityScenario()),
                new FlatWorldGoldenPathOperation(
                    "combat.player-respawn", "combat",
                    reset: ResetPlayerRespawnScenario,
                    onWorldReady: RunPlayerRespawnScenario,
                    beforeWorldExit: _ => AssertPlayerRespawnScenarioCompleted(),
                    cleanup: _ => CleanupPlayerRespawnScenario()),
                new FlatWorldGoldenPathOperation(
                    "map.chunk-load-speed", "map",
                    reset: ResetChunkLoadSpeedScenario,
                    onWorldReady: RunChunkLoadSpeedScenario,
                    beforeWorldExit: _ => AssertChunkLoadSpeedScenarioCompleted(),
                    cleanup: _ => CleanupChunkLoadSpeedScenario()),
                new FlatWorldGoldenPathOperation(
                    "item.drop-lifecycle", "item-module",
                    reset: ResetItemLifecycleScenario,
                    onWorldReady: BeginItemLifecycleScenario,
                    tickWorldReady: _ => TickItemLifecycleDropScenario(),
                    onTraversalTick: _ => TickItemLifecycleDropScenario(),
                    onChunkReady: VerifyItemLifecycleAtChunkReady,
                    beforeWorldExit: AssertItemLifecycleScenarioCompleted,
                    cleanup: _ => CleanupItemLifecycleScenario()),
                new FlatWorldGoldenPathOperation(
                    "environment.ecology", "environment",
                    reset: ResetEcologyScenario,
                    onWorldReady: BeginEcologyScenario,
                    onChunkReady: VerifyEcologyAtChunkReady,
                    beforeWorldExit: _ => AssertEcologyScenarioCompleted(),
                    cleanup: _ => CleanupEcologyScenario()),
                new FlatWorldGoldenPathOperation(
                    "building.placement", "building",
                    reset: ResetBuildingPlacementScenario,
                    onWorldReady: RunBuildingPlacementScenario,
                    tickWorldReady: _ => TickBuildingPlacementScenario(),
                    beforeWorldExit: _ => AssertBuildingPlacementScenarioCompleted(),
                    cleanup: _ => CleanupBuildingPlacementScenario()),
                new FlatWorldGoldenPathOperation(
                    "buff.burning", "buff",
                    reset: ResetBurningBuffScenario,
                    onTraversalTick: TickBurningBuffScenario,
                    onChunkReady: VerifyBurningBuffAtChunkReady,
                    beforeWorldExit: _ => AssertBurningBuffScenarioCompleted(),
                    cleanup: _ => CleanupBurningBuffScenario()),
                new FlatWorldGoldenPathOperation(
                    "save.auto", "data-save",
                    reset: ResetAutoSaveScenario,
                    tickWorldReady: context =>
                    {
                        BeginAutoSaveScenario(context);
                        return true;
                    },
                    onTraversalTick: TickAutoSaveScenario,
                    beforeWorldExit: _ => AssertAutoSaveScenarioCompleted(),
                    cleanup: _ => CleanupAutoSaveScenario()),
                new FlatWorldGoldenPathOperation(
                    "environment.tile-effects", "environment",
                    reset: ResetRuntimeTileEffectScenario,
                    onChunkReady: VerifyRuntimeTileEffectAtChunkReady,
                    beforeWorldExit: _ => AssertRuntimeTileEffectScenarioCompleted(),
                    cleanup: _ => CleanupRuntimeTileEffectScenario()),
                CreateInventoryCraftingOperation(),
                CreateCombatTargetDamageOperation(),
                CreateAudioPlaybackOperation(),
                CreateInventoryPanelOperation(),
                CreateDialogueSpeechOperation(),
                CreateEnvironmentTimeWeatherOperation(),
                CreateNavigationLoadedGridOperation()
            };
        }

        /// <summary>启动时验证注册表，避免新增操作的空 ID 或重复 ID 让 JSON 选择产生歧义。</summary>
        private static IReadOnlyList<IFlatWorldGoldenPathOperation> ValidateOperations(
            IReadOnlyList<IFlatWorldGoldenPathOperation> operations)
        {
            if (operations == null || operations.Count == 0)
                throw new InvalidOperationException("GoldenPath operation registry is empty.");

            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            foreach (IFlatWorldGoldenPathOperation operation in operations)
            {
                if (operation == null || string.IsNullOrWhiteSpace(operation.Id) ||
                    string.IsNullOrWhiteSpace(operation.SystemId))
                {
                    throw new InvalidOperationException(
                        "GoldenPath operation registry contains an invalid operation.");
                }
                if (!ids.Add(operation.Id))
                {
                    throw new InvalidOperationException(
                        $"GoldenPath operation registry contains duplicate ID {operation.Id}.");
                }
            }

            if (ids.Contains(WorldWrapOperationId))
            {
                throw new InvalidOperationException(
                    $"GoldenPath operation ID {WorldWrapOperationId} is reserved by the command flow.");
            }
            return operations;
        }

        #endregion

        #region 配置选择

        internal static IReadOnlyList<string> GetOperationIds() =>
            Operations.Select(operation => operation.Id)
                .Append(WorldWrapOperationId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal static IReadOnlyList<string> GetEnabledOperationIds(
            FlatWorldGoldenPathConfiguration configuration)
        {
            return GetOperationIds()
                .Where(id => IsOperationEnabled(configuration, id))
                .ToArray();
        }

        internal static bool IsOperationEnabled(
            FlatWorldGoldenPathConfiguration configuration,
            string operationId)
        {
            GoldenPathScenarioConfiguration scenarios = configuration?.scenarios;
            if (scenarios == null || string.IsNullOrWhiteSpace(operationId))
                return false;

            bool enabled = scenarios.enableAllOperations ||
                           ContainsOperationId(scenarios.enabledOperationIds, operationId);
            if (!enabled || ContainsOperationId(scenarios.disabledOperationIds, operationId))
                return false;

            if (string.Equals(operationId, "map.hydrology", StringComparison.OrdinalIgnoreCase))
                return scenarios.hydrology;
            if (string.Equals(operationId, "buff.burning", StringComparison.OrdinalIgnoreCase))
                return scenarios.burningBuff;
            if (string.Equals(operationId, WorldWrapOperationId, StringComparison.OrdinalIgnoreCase))
                return scenarios.worldWrap;
            return true;
        }

        internal static void ValidateOperationSelection(GoldenPathScenarioConfiguration scenarios)
        {
            if (scenarios == null)
                throw new InvalidOperationException("GoldenPath configuration: scenarios is null.");

            scenarios.enabledOperationIds ??= Array.Empty<string>();
            scenarios.disabledOperationIds ??= Array.Empty<string>();
            HashSet<string> known = new(GetOperationIds(), StringComparer.OrdinalIgnoreCase);
            ValidateOperationIds(scenarios.enabledOperationIds, "enabledOperationIds", known);
            ValidateOperationIds(scenarios.disabledOperationIds, "disabledOperationIds", known);

            HashSet<string> enabled = new(scenarios.enabledOperationIds, StringComparer.OrdinalIgnoreCase);
            string conflict = scenarios.disabledOperationIds.FirstOrDefault(enabled.Contains);
            if (!string.IsNullOrEmpty(conflict))
            {
                throw new InvalidOperationException(
                    $"GoldenPath configuration: operation {conflict} cannot be both enabled and disabled.");
            }

            if (!scenarios.enableAllOperations && scenarios.enabledOperationIds.Length == 0)
            {
                throw new InvalidOperationException(
                    "GoldenPath configuration: enabledOperationIds cannot be empty when " +
                    "enableAllOperations is false.");
            }
        }

        private static bool ContainsOperationId(IEnumerable<string> ids, string operationId) =>
            ids != null && ids.Any(id =>
                string.Equals(id, operationId, StringComparison.OrdinalIgnoreCase));

        private static void ValidateOperationIds(
            IEnumerable<string> ids,
            string fieldName,
            ISet<string> known)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || !known.Contains(id))
                {
                    throw new InvalidOperationException(
                        $"GoldenPath configuration: scenarios.{fieldName} contains unknown " +
                        $"operation {id ?? "<null>"}.");
                }
                if (!seen.Add(id))
                {
                    throw new InvalidOperationException(
                        $"GoldenPath configuration: scenarios.{fieldName} contains duplicate " +
                        $"operation {id}.");
                }
            }
        }

        private static IEnumerable<IFlatWorldGoldenPathOperation> GetEnabledOperations(
            FlatWorldGoldenPathScenarioContext context) =>
            Operations.Where(operation => IsOperationEnabled(context.Configuration, operation.Id));

        #endregion

        #region 生命周期分发

        private static void ResetRegisteredOperations()
        {
            FaultedOperationIds.Clear();
            foreach (IFlatWorldGoldenPathOperation operation in Operations)
                operation.Reset();
        }

        private static void RunRegisteredWorldReadyOperations(
            FlatWorldGoldenPathScenarioContext context)
        {
            foreach (IFlatWorldGoldenPathOperation operation in GetEnabledOperations(context))
                RunOperationSafely(operation, "OnWorldReady", () => operation.OnWorldReady(context));
        }

        private static bool TickRegisteredWorldReadyOperations(
            FlatWorldGoldenPathScenarioContext context)
        {
            bool completed = true;
            foreach (IFlatWorldGoldenPathOperation operation in GetEnabledOperations(context))
            {
                if (FaultedOperationIds.Contains(operation.Id))
                    continue;
                try
                {
                    completed &= operation.TickWorldReady(context);
                }
                catch (Exception exception)
                {
                    RecordOperationFailure(operation, "TickWorldReady", exception);
                }
            }
            return completed;
        }

        private static void RunRegisteredTraversalOperations(
            FlatWorldGoldenPathScenarioContext context)
        {
            foreach (IFlatWorldGoldenPathOperation operation in GetEnabledOperations(context))
                RunOperationSafely(operation, "OnTraversalTick",
                    () => operation.OnTraversalTick(context));
        }

        private static void RunRegisteredChunkReadyOperations(
            FlatWorldGoldenPathScenarioContext context)
        {
            foreach (IFlatWorldGoldenPathOperation operation in GetEnabledOperations(context))
                RunOperationSafely(operation, "OnChunkReady", () => operation.OnChunkReady(context));
        }

        private static void AssertRegisteredOperationsCompleted(
            FlatWorldGoldenPathScenarioContext context)
        {
            foreach (IFlatWorldGoldenPathOperation operation in GetEnabledOperations(context))
                RunOperationSafely(operation, "BeforeWorldExit",
                    () => operation.BeforeWorldExit(context));
        }

        private static void CleanupRegisteredOperations(
            FlatWorldGoldenPathScenarioContext context)
        {
            IFlatWorldGoldenPathOperation[] enabled = GetEnabledOperations(context).ToArray();
            for (int index = enabled.Length - 1; index >= 0; index--)
            {
                IFlatWorldGoldenPathOperation operation = enabled[index];
                try
                {
                    operation.Cleanup(context);
                }
                catch (Exception exception)
                {
                    RecordOperationFailure(operation, "Cleanup", exception);
                }
            }
        }

        /// <summary>隔离单项玩法失败，让同一轮仍能覆盖其他互不依赖的系统。</summary>
        private static void RunOperationSafely(
            IFlatWorldGoldenPathOperation operation,
            string phase,
            Action action)
        {
            if (FaultedOperationIds.Contains(operation.Id))
                return;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                RecordOperationFailure(operation, phase, exception);
            }
        }

        private static void RecordOperationFailure(
            IFlatWorldGoldenPathOperation operation,
            string phase,
            Exception exception)
        {
            FaultedOperationIds.Add(operation.Id);
            FlatWorldGoldenPathCommand.RecordRecoverableOperationFailure(
                operation.Id,
                operation.SystemId,
                phase,
                exception);
        }

        #endregion
    }
}
