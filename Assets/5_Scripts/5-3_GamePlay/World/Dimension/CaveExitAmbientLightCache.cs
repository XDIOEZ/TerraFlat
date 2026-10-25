using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>出口进度变化才更新采光点，玩家和拓扑不变时复用距离结果。</summary>
public sealed class CaveExitAmbientLightCache
{
    #region 采光缓存

    // 洞口自然采光在20米内线性衰减，强度仍由昼夜系统独立决定。
    private const float FadeDistance = 20f;
    private readonly List<Vector2> exitPositions = new();
    private GameSaveData cachedSave;
    private WorldAddress cachedAddress;
    private long cachedRevision = -1;
    private Vector2 cachedPosition;
    private WorldTopologyDomain cachedTopology;
    private float cachedFactor;
    private bool hasFactor;

    public int PositionsRefreshCount { get; private set; }
    public int DistanceEvaluationCount { get; private set; }

    public float Evaluate(GameSaveData save, WorldAddress cave, Vector2 position, WorldTopologyDomain topology)
    {
        long revision = DimensionTravelProgressStore.GetCaveExitLightingRevision(save);
        if (!ReferenceEquals(cachedSave, save) || cachedAddress != cave || cachedRevision != revision)
        {
            exitPositions.Clear();
            DimensionTravelProgressStore.CollectOpenCaveExitPositions(save, cave, exitPositions);
            cachedSave = save;
            cachedAddress = cave;
            cachedRevision = revision;
            hasFactor = false;
            PositionsRefreshCount++;
        }

        if (hasFactor && cachedPosition.Equals(position) &&
            cachedTopology.IsWrappedValue == topology.IsWrappedValue &&
            cachedTopology.Min.Equals(topology.Min) && cachedTopology.Span.Equals(topology.Span))
            return cachedFactor;

        float nearestSquared = float.PositiveInfinity;
        var origin = new float2(position.x, position.y);
        for (int i = 0; i < exitPositions.Count; i++)
        {
            Vector2 exit = exitPositions[i];
            nearestSquared = Mathf.Min(nearestSquared, topology.SqrDistance(origin, new float2(exit.x, exit.y)));
        }

        cachedFactor = Mathf.Clamp01(1f - Mathf.Sqrt(nearestSquared) / FadeDistance);
        cachedPosition = position;
        cachedTopology = topology;
        hasFactor = true;
        DistanceEvaluationCount++;
        return cachedFactor;
    }

    public void Clear()
    {
        cachedSave = null;
        cachedAddress = default;
        cachedRevision = -1;
        exitPositions.Clear();
        hasFactor = false;
    }

    #endregion
}
