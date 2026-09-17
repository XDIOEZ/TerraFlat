using System.Collections.Generic;
using FlatWorld.Navigation;
using Unity.Mathematics;
using UnityEngine;

/// <summary>管理器的共享流场适配部分；旧请求与 ECS 缓存共同读取一个权威网格。</summary>
public sealed partial class WorldNavigationManager
{
    #region ECS 共享导航所有权
    // 只有实际使用 ECS 导航时才创建；旧 GameObject Agent 保持现有请求路径。
    private FlowNavigationCache sharedNavigation;

    /// <summary>只验证共享缓存所有权；观察者和旧模拟检查世界时不能隐式创建新缓存。</summary>
    public bool OwnsSharedNavigation(FlowNavigationCache cache) =>
        cache != null && ReferenceEquals(sharedNavigation, cache);

    /// <summary>取得当前世界唯一共享流场；调用方只借用视图，释放由本管理器负责。</summary>
    public FlowNavigationCache GetSharedNavigation()
    {
        if (sharedNavigation != null) return sharedNavigation;
        sharedNavigation = new FlowNavigationCache(new SharedGridSource(grid));
        grid.CellChanged += MarkSharedNavigationDirty;
        grid.Cleared += sharedNavigation.RequestReset;
        return sharedNavigation;
    }

    /// <summary>把最终网格的变化定向传给流场缓存，不读取 Collider 或重复解析地形。</summary>
    private void MarkSharedNavigationDirty(Vector2Int cell)
    {
        sharedNavigation.MarkCellDirty(new int2(cell.x, cell.y));
    }

    /// <summary>世界切换和销毁时等待读取 Job 并释放缓存，失效旧世界目标句柄。</summary>
    private void DisposeSharedNavigation()
    {
        if (sharedNavigation == null) return;
        grid.CellChanged -= MarkSharedNavigationDirty;
        grid.Cleared -= sharedNavigation.RequestReset;
        sharedNavigation.Dispose(); sharedNavigation = null;
    }

    /// <summary>主线程适配器，复用最终 WorldNavigationGrid 权重、建筑覆盖与循环坐标。</summary>
    private sealed class SharedGridSource : IFlowGridSource
    {
        private readonly WorldNavigationGrid source;
        private readonly List<Vector2Int> positions = new();
        private readonly HashSet<int2> uniqueChunks = new();
        public WorldTopologyDomain Domain => WorldTopologyRuntime.GetActiveDomain();

        /// <summary>绑定当前管理器拥有的网格。</summary>
        internal SharedGridSource(WorldNavigationGrid source) { this.source = source; }

        /// <summary>读取最终有效权重与水面状态；未加载格不参与导航，阻挡格以 0 表达。</summary>
        public bool TryGetCell(int2 cell, out FlowNavigationCellData data)
        {
            bool registered = source.TryGetCell(new Vector2Int(cell.x, cell.y), out WorldNavigationCell sourceCell);
            data = new FlowNavigationCellData
            {
                Penalty = registered && sourceCell.Walkable ? sourceCell.Penalty : 0u,
                Water = (byte)(registered && sourceCell.Water ? 1 : 0),
                WaterDepth = registered && sourceCell.Water ? sourceCell.WaterDepth : 0f
            };
            return registered;
        }

        /// <summary>首次绑定时从已注册格收集唯一 Chunk，不修改旧路径变化队列。</summary>
        public void CollectChunks(List<int2> result)
        {
            source.CopyCellPositions(positions); uniqueChunks.Clear(); result.Clear();
            WorldTopologyDomain domain = Domain;
            foreach (Vector2Int cell in positions)
            {
                int2 chunk = FlowNavigationMath.ChunkOf(new int2(cell.x, cell.y), domain);
                if (uniqueChunks.Add(chunk)) result.Add(chunk);
            }
            positions.Clear(); uniqueChunks.Clear();
        }
    }
    #endregion
}
