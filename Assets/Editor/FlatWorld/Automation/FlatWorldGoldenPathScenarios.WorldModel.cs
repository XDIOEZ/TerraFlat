using System;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Automation
{
    /// <summary>Exercises the real headless chunk lease lifecycle through the local presentation window.</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static RuntimeWorldAddress _modelStartAddress;
        private static ChunkRuntime _modelStartChunk;
        private static ulong _modelTerrainHash;
        private static Vector2 _modelStartPosition;
        private static Vector2 _modelAwayPosition;
        private static bool _modelDormancyObserved;
        private static bool _modelRebindObserved;

        internal static Vector2 WorldModelAwayPosition => _modelAwayPosition;
        internal static Vector2 WorldModelStartPosition => _modelStartPosition;

        private static void ResetWorldModelScenario()
        {
            _modelStartAddress = default;
            _modelStartChunk = null;
            _modelTerrainHash = 0;
            _modelStartPosition = default;
            _modelAwayPosition = default;
            _modelDormancyObserved = false;
            _modelRebindObserved = false;
        }

        private static void BeginWorldModelScenario(FlatWorldGoldenPathScenarioContext context)
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.WorldRuntime == null)
                throw new InvalidOperationException("无头世界模型未在进入世界时创建。");

            ChunkGenerationSettingsSnapshot settings = manager.ActiveGenerationProfile?.Settings;
            if (settings == null)
                throw new InvalidOperationException("无头世界模型缺少当前生成参数快照。");
            double expectedScale = context.Configuration.world.noiseScale;
            double expectedDistanceScale = expectedScale <= 0d
                ? 4d
                : Math.Max(0.25d, Math.Min(4d, 0.01d / expectedScale));
            if (Math.Abs(settings.WorldCoordinateScale - expectedScale) > 0.000001d ||
                Math.Abs(settings.WorldCoordinateDistanceScale - expectedDistanceScale) > 0.000001d)
            {
                throw new InvalidOperationException(
                    $"世界坐标缩放未进入区块与河流生成：配置={expectedScale:F6}, " +
                    $"Profile={settings.WorldCoordinateScale:F6}, " +
                    $"河流距离倍率={settings.WorldCoordinateDistanceScale:F3}。");
            }

            _modelStartPosition = context.CurrentPosition;
        }

        private static void CaptureWorldModelBaseline()
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.WorldRuntime == null)
                throw new InvalidOperationException("无头世界模型在往返前已销毁。");
            Mover mover = ItemMgr.Instance?.User_Player?.itemMods?.GetMod_ByID<Mover>(ModText.Mover);
            if (mover?.rb == null)
                throw new InvalidOperationException("无头模型往返前找不到玩家位置。");

            _modelStartPosition = mover.rb.position;
            _modelStartAddress = manager.ResolveWorldAddress(_modelStartPosition);
            if (!manager.TryGetChunkRuntime(_modelStartAddress, out _modelStartChunk) ||
                _modelStartChunk.DataStatus != ChunkDataStatus.Ready ||
                _modelStartChunk.SimulationStatus != ChunkSimulationStatus.Active ||
                _modelStartChunk.PresentationStatus != ChunkPresentationStatus.Bound ||
                !manager.TryGetRuntimeChunkView(_modelStartAddress, out ChunkView view))
            {
                throw new InvalidOperationException($"起始无头区块未完成数据、模拟和表现绑定：{_modelStartAddress}。");
            }

            _modelTerrainHash = _modelStartChunk.Terrain.ComputeStableHash();

            Debug.Log(
                $"[GoldenPath][WorldModel] 已记录起始区块 {_modelStartAddress}，" +
                $"hash={_modelTerrainHash}。");
        }

        internal static void BeginWorldModelExcursion(Vector2 direction, Vector2 chunkSize)
        {
            CaptureWorldModelBaseline();
            Mod_ChunkLoader loader = FlatWorldGoldenPathCommandPlayerLoader();
            int dormantDistance = loader.CurrentLoadChunkDistance;
            if (dormantDistance >= loader.CurrentDestroyChunkDistance)
            {
                throw new InvalidOperationException(
                    $"区块窗口没有可验证的休眠带：load={dormantDistance}, " +
                    $"destroy={loader.CurrentDestroyChunkDistance}。");
            }

            direction = SelectSafeCardinalDirection(_modelStartPosition);
            float distance = (direction.x != 0f ? chunkSize.x : chunkSize.y) *
                             (dormantDistance + 0.05f);
            _modelAwayPosition = WorldTopologyRuntime.NormalizePosition(
                _modelStartPosition + direction * distance);
        }

        private static Mod_ChunkLoader FlatWorldGoldenPathCommandPlayerLoader()
        {
            Player player = ItemMgr.Instance?.User_Player;
            Mod_ChunkLoader loader = player?.itemMods?.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
            return loader ?? throw new InvalidOperationException("无头模型往返场景找不到 ChunkLoader。");
        }

        private static Vector2 SelectSafeCardinalDirection(Vector2 position)
        {
            if (!WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
                return Vector2.right;
            float midpoint = (bounds.Min.x + bounds.MaxExclusive.x) * 0.5f;
            return position.x <= midpoint ? Vector2.right : Vector2.left;
        }

        internal static bool TickWorldModelAway()
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.WorldRuntime == null)
                return false;
            if (!manager.TryGetChunkRuntime(_modelStartAddress, out ChunkRuntime retained))
                throw new InvalidOperationException($"起始无头区块在 destroyDistance 内被错误逐出：{_modelStartAddress}。");
            if (!ReferenceEquals(retained, _modelStartChunk))
                throw new InvalidOperationException("起始无头区块对象在休眠阶段被替换。");
            if (retained.SimulationStatus != ChunkSimulationStatus.Dormant ||
                retained.PresentationStatus != ChunkPresentationStatus.Unbound)
                return false;
            if (retained.SimulationLeaseCount != 0 || retained.PresentationLeaseCount != 0 ||
                retained.NavigationLeaseCount != 0)
                throw new InvalidOperationException("休眠区块仍持有模拟、表现或导航租约。");
            AssertWorldModelIdentity(retained);
            _modelDormancyObserved = true;
            Debug.Log("[GoldenPath][WorldModel] 起始区块已失去 View 并保留无头模型，Tick 已休眠。");
            return true;
        }

        internal static bool TickWorldModelReturn()
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.WorldRuntime == null ||
                !manager.TryGetChunkRuntime(_modelStartAddress, out ChunkRuntime rebound) ||
                rebound.DataStatus != ChunkDataStatus.Ready ||
                rebound.SimulationStatus != ChunkSimulationStatus.Active ||
                rebound.PresentationStatus != ChunkPresentationStatus.Bound ||
                !manager.TryGetRuntimeChunkView(_modelStartAddress, out ChunkView view))
            {
                return false;
            }

            if (!ReferenceEquals(rebound, _modelStartChunk))
                throw new InvalidOperationException("返回起点后无头区块对象未复用。");
            AssertWorldModelIdentity(rebound);
            if (rebound.SimulationLeaseCount != 1 || rebound.PresentationLeaseCount != 1 ||
                rebound.NavigationLeaseCount != 1 || !rebound.HasNavigationLease)
                throw new InvalidOperationException("返回起点后的模拟、表现或导航租约发生重复。");
            WorldEventBus events = manager.WorldRuntime.Events;
            int boundViewCount = 0;
            foreach (ChunkRuntime chunk in manager.Chunks.Values)
            {
                if (chunk.PresentationStatus == ChunkPresentationStatus.Bound)
                    boundViewCount++;
            }
            if (events.SubscriptionCount<ChunkCommitted>() != boundViewCount)
            {
                throw new InvalidOperationException(
                    $"返回起点后的区块事件订阅与已绑定 View 不一致：" +
                    $"subscriptions={events.SubscriptionCount<ChunkCommitted>()}, " +
                    $"views={boundViewCount}。");
            }

            _modelRebindObserved = true;
            Debug.Log("[GoldenPath][WorldModel] 返回起点重绑通过：地形、导航和订阅均无重复。");
            return true;
        }

        private static void AssertWorldModelIdentity(ChunkRuntime chunk)
        {
            if (chunk.Terrain == null || chunk.Terrain.ComputeStableHash() != _modelTerrainHash)
                throw new InvalidOperationException("起始无头区块在休眠/重绑后地形哈希改变。");
        }

        private static void AssertWorldModelScenarioCompleted()
        {
            if (!_modelDormancyObserved || !_modelRebindObserved)
                throw new InvalidOperationException("完整黄金路径结束前未完成无头区块休眠与重绑验证。");
        }

        private static void CleanupWorldModelScenario()
        {
            _modelStartChunk = null;
        }
    }
}
