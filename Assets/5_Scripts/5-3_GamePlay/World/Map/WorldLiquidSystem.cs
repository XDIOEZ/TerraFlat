using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 世界液体的权威玩法入口。泵、倾倒和 MOD 均在这里修改 0～10 整数等级；
/// 修改后立即记录稳定 LiquidId 差量，地面与水文数据完全不变。
/// </summary>
public static class WorldLiquidSystem
{
    #region 查询
    private static Action<RuntimeTerrainTileSample> liquidAddedSubscribers;
    private static Action<RuntimeTerrainTileSample> liquidPumpedSubscribers;
    private static Delegate[] liquidAddedSnapshot = Array.Empty<Delegate>();
    private static Delegate[] liquidPumpedSnapshot = Array.Empty<Delegate>();

    // 监听器增删时才生成一次调用快照，频繁倒水/取水不再逐次分配委托数组。
    public static event Action<RuntimeTerrainTileSample> SurfaceLiquidAdded
    {
        add { liquidAddedSubscribers += value; liquidAddedSnapshot = SnapshotSubscribers(liquidAddedSubscribers); }
        remove { liquidAddedSubscribers -= value; liquidAddedSnapshot = SnapshotSubscribers(liquidAddedSubscribers); }
    }

    public static event Action<RuntimeTerrainTileSample> SurfaceLiquidPumped
    {
        add { liquidPumpedSubscribers += value; liquidPumpedSnapshot = SnapshotSubscribers(liquidPumpedSubscribers); }
        remove { liquidPumpedSubscribers -= value; liquidPumpedSnapshot = SnapshotSubscribers(liquidPumpedSubscribers); }
    }

    private static Delegate[] SnapshotSubscribers(Action<RuntimeTerrainTileSample> subscribers)
        => subscribers?.GetInvocationList() ?? Array.Empty<Delegate>();
    public static bool TryGetDefinition(RuntimeTerrainTileSample sample, out LiquidDefinition definition)
    {
        definition = null;
        string id = sample.Terrain?.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        return !string.IsNullOrEmpty(id) && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetLiquidDefinition(id, out definition) && definition.WorldWater != null;
    }

    /// <summary>仅有效支撑面可以遮断浸水；真实液体仍保存在底部并可被抽取。</summary>
    public static float GetSurfaceDepth(ChunkTerrainData terrain, int x, int y) =>
        LiquidCellValue.ToNormalizedDepth(GetSurfaceLevel(terrain, x, y));

    public static int GetSurfaceLevel(ChunkTerrainData terrain, int x, int y) =>
        TerrainSupportLayer.GetTileId(terrain, x, y) == 0 ? terrain.GetLiquidLevel(x, y) : 0;

    /// <summary>寻路读取当前液体定义的高成本，抽干后自动恢复 Ground 原成本。</summary>
    public static uint GetNavigationCost(ChunkTerrainData terrain, int x, int y, uint groundCost)
        => GetNavigationCost(terrain, x, y, groundCost, GetSurfaceLevel(terrain, x, y));

    // 导航已采样当前水位时直接复用，不重复检查平台遮水和液体等级。
    internal static uint GetNavigationCost(ChunkTerrainData terrain, int x, int y, uint groundCost, int surfaceLevel)
    {
        if (surfaceLevel <= 0) return groundCost;
        string id = terrain.GetLiquidId(x, y);
        if (GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition) &&
            definition.WorldWater != null)
            return Math.Max(groundCost, (uint)definition.WorldWater.NavigationCost);
        throw new InvalidOperationException($"世界液体缺少玩法定义：{id}");
    }
    #endregion

    #region 权威修改
    /// <summary>只向可接收的世界格加入同种液体，满格、平台和异种液体均不吞掉余量。</summary>
    public static bool TryPour(Vector2 worldPosition, string liquidId, int requestedLevel, out int addedLevel)
    {
        addedLevel = 0;
        if (!GameNetwork.HasStateAuthority || requestedLevel <= 0 ||
            ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample) ||
            sample.Terrain == null || sample.Terrain.IsDisposed || sample.Cell.BlockingTileId != 0 ||
            TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0)
            return false;
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        int current = sample.Terrain.GetLiquidLevel(x, y);
        if (current > 0 && !string.Equals(sample.Terrain.GetLiquidId(x, y), liquidId, StringComparison.OrdinalIgnoreCase))
            return false;
        // 整批接收才能成功，一份液体始终只增加一级。
        if (requestedLevel > LiquidCellValue.MaximumLevel - current || !TrySet(sample, liquidId, current + requestedLevel))
            return false;
        addedLevel = requestedLevel;
        NotifySurfaceLiquidAdded(sample);
        return true;
    }

    private static void NotifySurfaceLiquidAdded(RuntimeTerrainTileSample sample)
        => PublishSurfaceLiquidEvent(liquidAddedSnapshot, sample);

    /// <summary>抽水事务全部提交后发布，取水后的派生规则不得提前改变回滚现场。</summary>
    public static void PublishSurfaceLiquidPumped(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed) return;
        PublishSurfaceLiquidEvent(liquidPumpedSnapshot, sample);
    }

    private static void PublishSurfaceLiquidEvent(Delegate[] handlers, RuntimeTerrainTileSample sample)
    {
        // 本轮取快照，回调期间增删订阅只影响下一次通知。
        // 接收方异常不能把已写入的水变成失败事务，让来源重复恢复同一份水。
        foreach (Action<RuntimeTerrainTileSample> handler in handlers)
        {
            try { handler(sample); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }

    public static bool TrySet(Vector2 worldPosition, string liquidId, int liquidLevel)
    {
        if (!GameNetwork.HasStateAuthority || ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample)) return false;
        return TrySet(sample, liquidId, liquidLevel);
    }

    public static bool TrySet(RuntimeTerrainTileSample sample, string liquidId, int liquidLevel)
    {
        if (!GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed) return false;
        int level = LiquidCellValue.NormalizeLevel(liquidLevel);
        if (level > 0 && (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out var definition) || definition.WorldWater == null)) return false;
        int index = level > 0 ? sample.Terrain.LiquidTypes.GetIndex(liquidId) : 0;
        if (!sample.Terrain.SetLiquid(sample.LocalCell.x, sample.LocalCell.y, index, level)) return false;
        SaveDataMgr.Instance?.RecordLiquidChange(sample);
        return true;
    }

    /// <summary>抽取有限液深，返回实际抽取量；没有水时不造水、不改变底部 Ground。</summary>
    public static bool TryPump(Vector2 worldPosition, int requestedLevel, out string liquidId, out int removedLevel)
    {
        liquidId = null;
        removedLevel = 0;
        if (!GameNetwork.HasStateAuthority || requestedLevel <= 0 ||
            ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample)) return false;
        return TryPump(sample, requestedLevel, out liquidId, out removedLevel);
    }

    /// <summary>持有区块采样的机械节点复用此入口，减少重复查询；校验仍由统一修改接口负责。</summary>
    public static bool TryPump(RuntimeTerrainTileSample sample, int requestedLevel, out string liquidId, out int removedLevel)
        => TryPumpPreservingMinimumLevel(sample, requestedLevel, 0, out liquidId, out removedLevel);

    /// <summary>共享水域取余水时在正式扣量处再次保留底水，不能依赖先前查询的水量快照。</summary>
    public static bool TryPumpPreservingMinimumLevel(Vector2 position, int requestedLevel, int minimumRemainingLevel,
        out string liquidId, out int removedLevel)
    {
        liquidId = null; removedLevel = 0;
        return ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out var sample) &&
            TryPumpPreservingMinimumLevel(sample, requestedLevel, minimumRemainingLevel, out liquidId, out removedLevel);
    }

    public static bool TryPumpPreservingMinimumLevel(RuntimeTerrainTileSample sample, int requestedLevel, int minimumRemainingLevel,
        out string liquidId, out int removedLevel)
    {
        liquidId = null; removedLevel = 0;
        if (!GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed ||
            requestedLevel <= 0 || (uint)minimumRemainingLevel > LiquidCellValue.MaximumLevel) return false;
        int level = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);
        if (level <= minimumRemainingLevel) return false;
        liquidId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        int removed = Math.Min(requestedLevel, level - minimumRemainingLevel);
        if (!TrySet(sample, liquidId, level - removed)) return false;
        removedLevel = removed;
        return true;
    }
    #endregion
}
