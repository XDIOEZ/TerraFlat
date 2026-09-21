using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 世界液体的权威玩法入口。泵、倾倒和 MOD 均在这里修改 0～1 液深；
/// 修改后立即记录稳定 LiquidId 差量，地面与水文数据完全不变。
/// </summary>
public static class WorldLiquidSystem
{
    #region 查询
    public static bool TryGetDefinition(RuntimeTerrainTileSample sample, out LiquidDefinition definition)
    {
        definition = null;
        string id = sample.Terrain?.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        return !string.IsNullOrEmpty(id) && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetLiquidDefinition(id, out definition) && definition.WorldWater != null;
    }

    /// <summary>仅有效支撑面可以遮断浸水；真实液体仍保存在底部并可被抽取。</summary>
    public static float GetSurfaceDepth(ChunkTerrainData terrain, int x, int y) =>
        TerrainSupportLayer.GetTileId(terrain, x, y) == 0 ? terrain.GetLiquidDepth(x, y) : 0f;

    /// <summary>寻路读取当前液体定义的高成本，抽干后自动恢复 Ground 原成本。</summary>
    public static uint GetNavigationCost(ChunkTerrainData terrain, int x, int y, uint groundCost)
    {
        if (GetSurfaceDepth(terrain, x, y) <= 0f) return groundCost;
        string id = terrain.GetLiquidId(x, y);
        if (GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition) &&
            definition.WorldWater != null)
            return Math.Max(groundCost, (uint)definition.WorldWater.NavigationCost);
        throw new InvalidOperationException($"世界液体缺少玩法定义：{id}");
    }
    #endregion

    #region 权威修改
    public static bool TrySet(Vector2 worldPosition, string liquidId, float liquidDepth)
    {
        if (!GameNetwork.HasStateAuthority || ChunkMgr.ExistingInstance == null ||
            float.IsNaN(liquidDepth) || float.IsInfinity(liquidDepth) ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample)) return false;
        return TrySet(sample, liquidId, liquidDepth);
    }

    public static bool TrySet(RuntimeTerrainTileSample sample, string liquidId, float liquidDepth)
    {
        if (!GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed ||
            float.IsNaN(liquidDepth) || float.IsInfinity(liquidDepth)) return false;
        float depth = Mathf.Clamp01(liquidDepth);
        if (depth > 0f && (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetLiquidDefinition(liquidId, out var definition) || definition.WorldWater == null)) return false;
        int index = depth > 0f ? sample.Terrain.LiquidTypes.GetIndex(liquidId) : 0;
        if (!sample.Terrain.SetLiquid(sample.LocalCell.x, sample.LocalCell.y, index, depth)) return false;
        SaveDataMgr.Instance?.RecordLiquidChange(sample);
        return true;
    }

    /// <summary>抽取有限液深，返回实际抽取量；没有水时不造水、不改变底部 Ground。</summary>
    public static bool TryPump(Vector2 worldPosition, float requestedDepth, out string liquidId, out float removedDepth)
    {
        liquidId = null;
        removedDepth = 0f;
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(requestedDepth) || requestedDepth <= 0f ||
            ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var sample)) return false;
        return TryPump(sample, requestedDepth, out liquidId, out removedDepth);
    }

    /// <summary>持有区块采样的机械节点复用此入口，减少重复查询；校验仍由统一修改接口负责。</summary>
    public static bool TryPump(RuntimeTerrainTileSample sample, float requestedDepth, out string liquidId, out float removedDepth)
    {
        liquidId = null; removedDepth = 0f;
        if (!GameNetwork.HasStateAuthority || sample.Terrain == null || sample.Terrain.IsDisposed ||
            !float.IsFinite(requestedDepth) || requestedDepth <= 0f) return false;
        float depth = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
        if (depth <= 0f) return false;
        liquidId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        float removed = Mathf.Min(requestedDepth, depth);
        if (!TrySet(sample, liquidId, depth - removed)) return false;
        removedDepth = removed;
        return true;
    }
    #endregion
}
