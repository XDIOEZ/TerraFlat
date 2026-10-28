using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>按区块缓存可听河流格，并只在权威地形变化时更新候选。</summary>
public sealed class RiverAmbientAudioChunkIndex : IDisposable
{
    #region 候选索引
    private readonly ChunkTerrainData terrain;
    private readonly Func<string, bool> isWaterContactLiquid;
    private readonly Dictionary<int, float> candidates = new();
    private bool disposed;

    public long Revision { get; private set; }
    public int CandidateCount => candidates.Count;

    public RiverAmbientAudioChunkIndex(ChunkTerrainData terrain, Func<string, bool> isWaterContactLiquid)
    {
        this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        this.isWaterContactLiquid = isWaterContactLiquid ?? throw new ArgumentNullException(nameof(isWaterContactLiquid));
        if (!terrain.IsDisposed)
        {
            for (int index = 0; index < terrain.CellCount; index++)
                RefreshCandidate(index);
            terrain.Changed += HandleTerrainChanged;
            terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        }
    }
    #endregion

    #region 最近河流查询
    public bool TryFindNearest(Vector2 listenerLocalPosition, float radius,
        out Vector2 localPosition, out float strength)
    {
        localPosition = default;
        strength = 0f;
        if (disposed || terrain.IsDisposed) return false;

        float nearestDistanceSquared = radius * radius;
        bool found = false;
        foreach (KeyValuePair<int, float> candidate in candidates)
        {
            int x = candidate.Key % terrain.Width;
            int y = candidate.Key / terrain.Width;
            Vector2 candidatePosition = new(x + 0.5f, y + 0.5f);
            float distanceSquared = (candidatePosition - listenerLocalPosition).sqrMagnitude;
            if (distanceSquared > nearestDistanceSquared) continue;
            nearestDistanceSquared = distanceSquared;
            localPosition = candidatePosition;
            strength = candidate.Value;
            found = true;
        }
        return found;
    }
    #endregion

    #region 河流声强规则
    public static float ResolveRiverCellStrength(ChunkTerrainData terrain, int x, int y,
        Func<string, bool> isWaterContactLiquid)
    {
        if (terrain.IsDisposed ||
            !terrain.TryGetEnvironmentValue("riverKind", x, y, out float riverKind) ||
            Mathf.RoundToInt(riverKind) != 1 ||
            !terrain.TryGetEnvironmentValue("riverFlow", x, y, out float flow) ||
            !float.IsFinite(flow) || flow <= 0f)
            return 0f;

        float depth;
        float iceOpenFactor = 1f;
        string liquidId;
        if (FrozenSurfaceWaterLayer.TryReadSource(terrain, x, y, out FrozenSurfaceWaterSource frozen) &&
            terrain.GetCell(x, y).GroundTileId == frozen.IceTileId)
        {
            liquidId = terrain.LiquidTypes.GetId(frozen.LiquidTypeIndex);
            depth = LiquidCellValue.ToNormalizedDepth(frozen.LiquidLevel);
            iceOpenFactor = terrain.TryGetEnvironmentValue(
                FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, out float meltProgress)
                ? Mathf.Clamp01(meltProgress) : 0f;
        }
        else
        {
            depth = terrain.GetLiquidDepth(x, y);
            if (depth <= 0f) return 0f;
            liquidId = terrain.GetLiquidId(x, y);
        }

        if (!isWaterContactLiquid(liquidId)) return 0f;

        float flowFactor = WaterEnvironmentRules.ResolveRiverStrength(flow) * 0.5f;
        return Mathf.Clamp01(depth * flowFactor * iceOpenFactor);
    }
    #endregion

    #region 增量候选维护
    private void HandleTerrainChanged(ChunkTerrainChanged change)
    {
        if (disposed || !IsRelevantChange(change)) return;
        foreach (int index in change.GetCellIndices(terrain.Width))
            RefreshCandidate(index);
    }

    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged change)
    {
        if (disposed || !ReferenceEquals(change.Terrain, terrain)) return;
        ReadOnlySpan<int> indices = change.CellIndices.Span;
        for (int i = 0; i < indices.Length; i++)
            RefreshCandidate(indices[i]);
    }

    private static bool IsRelevantChange(ChunkTerrainChanged change)
    {
        if (change.Kind == TerrainChangeKind.Cell || change.Kind == TerrainChangeKind.Liquid)
            return true;
        if (change.Kind != TerrainChangeKind.Environment) return false;
        return change.AffectsEnvironmentLayer("riverKind") ||
               change.AffectsEnvironmentLayer("riverFlow") ||
               change.AffectsEnvironmentLayer(FrozenSurfaceWaterLayer.IceTileLayer) ||
               change.AffectsEnvironmentLayer(FrozenSurfaceWaterLayer.LiquidTypeLayer) ||
               change.AffectsEnvironmentLayer(FrozenSurfaceWaterLayer.LiquidLevelLayer) ||
               change.AffectsEnvironmentLayer(FrozenSurfaceWaterLayer.MeltProgressLayer);
    }

    private void RefreshCandidate(int index)
    {
        float next = ResolveRiverCellStrength(terrain, index % terrain.Width, index / terrain.Width,
            isWaterContactLiquid);
        bool hadCandidate = candidates.TryGetValue(index, out float previous);
        if (next > 0f)
        {
            if (hadCandidate && previous.Equals(next)) return;
            candidates[index] = next;
            Revision++;
        }
        else if (hadCandidate)
        {
            candidates.Remove(index);
            Revision++;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!terrain.IsDisposed)
        {
            terrain.Changed -= HandleTerrainChanged;
            terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        candidates.Clear();
    }
    #endregion
}
