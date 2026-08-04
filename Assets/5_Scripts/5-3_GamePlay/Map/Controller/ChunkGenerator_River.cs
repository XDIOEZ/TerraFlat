using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Performance-first river generator.
/// Rivers are deterministic, world-space curved bands. The generator does one
/// pass over the chunk and does not build hydrology halos, flow maps or lakes.
/// </summary>
[Serializable]
public sealed class ChunkGenerator_River : ChunkGeneratorBase
{
    private const float SeaSalt = 80f;

    [Header("Dependencies")]
    public Tilemap targetTilemap;
    public Tile_Block riverTileBlock;

    [Header("Fast river shape")]
    public int seed = 12345;

    [Min(8f)]
    [Tooltip("Distance between world-space river bands.")]
    public float channelSpacing = 128f;

    [Range(0.5f, 6f)]
    [Tooltip("Base half-width of a river in tiles.")]
    public float channelHalfWidth = 1.4f;

    [Range(0f, 48f)]
    public float bendAmplitude = 16f;

    [Range(0.0005f, 0.05f)]
    public float bendFrequency = 0.006f;

    [Range(0f, 1f)]
    public float widthVariation = 0.3f;

    [Tooltip("Approximate direction in which rivers flow.")]
    public Vector2 flowDirection = new Vector2(0.35f, 0.94f);

    [Header("Water data")]
    public RiverWriteMode writeMode = RiverWriteMode.ReplaceTop;

    [Range(0f, 1f)]
    public float riverDepthMin = 0.2f;

    [Range(0f, 1f)]
    public float riverDepthMax = 0.9f;

    [Tooltip("Disabled by default because decorative GameObjects are much more expensive than river tiles.")]
    public bool spawnRiverStones;

    [NonSerialized] private int activeWorldSeed = 1;
    [NonSerialized] private bool hasLoggedInvalidTile;

    public enum RiverWriteMode
    {
        ReplaceTop,
        AddLayer
    }

    [Button("Generate Rivers")]
    public override void Generate(MapGenerationContext context)
    {
        IEnumerator routine = GenerateCells(context, int.MaxValue);
        while (routine.MoveNext())
        {
        }
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        return GenerateCells(context, Mathf.Max(1, workBatchSize));
    }

    /// <summary>
    /// Pure deterministic query used by generation and tests. It is independent
    /// of chunk boundaries, so adjacent chunks always agree on edge cells.
    /// </summary>
    public bool TryEvaluateRiverCell(Vector2Int worldPosition, out float depth)
    {
        return TryEvaluateRiverCell(worldPosition, activeWorldSeed, out depth);
    }

    /// <summary>
    /// 以显式世界种子纯计算河流格。出生定位在 Chunk 尚未创建时调用它，
    /// 以排除随后会被河流管线覆盖的候选陆地。
    /// </summary>
    public bool TryEvaluateRiverCell(Vector2Int worldPosition, int worldSeed, out float depth)
    {
        Vector2 direction = flowDirection.sqrMagnitude > 0.0001f
            ? flowDirection.normalized
            : Vector2.up;
        Vector2 normal = new Vector2(-direction.y, direction.x);
        Vector2 position = worldPosition;

        float along = Vector2.Dot(position, direction);
        float across = Vector2.Dot(position, normal);
        float seedOffset = (GetGenerationSeed(worldSeed) & 0xFFFF) * 0.01337f;

        float bend = (Mathf.PerlinNoise(
                          along * Mathf.Max(0.0005f, bendFrequency) + seedOffset,
                          seedOffset * 0.731f) - 0.5f) * 2f * Mathf.Max(0f, bendAmplitude);
        float spacing = Mathf.Max(8f, channelSpacing);
        float wrapped = Mathf.Repeat(across + bend + spacing * 0.5f, spacing) - spacing * 0.5f;
        float distance = Mathf.Abs(wrapped);

        float widthNoise = Mathf.PerlinNoise(
            along * Mathf.Max(0.0005f, bendFrequency) * 1.9f + seedOffset * 0.37f,
            seedOffset * 1.117f);
        float halfWidth = Mathf.Max(0.5f, channelHalfWidth) *
                          Mathf.Lerp(1f - Mathf.Clamp01(widthVariation),
                              1f + Mathf.Clamp01(widthVariation), widthNoise);

        if (distance > halfWidth)
        {
            depth = 0f;
            return false;
        }

        float centerStrength = 1f - Mathf.Clamp01(distance / Mathf.Max(0.001f, halfWidth));
        depth = Mathf.Lerp(
            Mathf.Clamp01(riverDepthMin),
            Mathf.Clamp01(riverDepthMax),
            centerStrength * centerStrength);
        return true;
    }

    private IEnumerator GenerateCells(MapGenerationContext context, int maxCellsPerFrame)
    {
        if (!TryPrepareGeneration(context, out int width, out int height))
            yield break;

        Vector2Int origin = Map.Data.position;
        var budget = new ChunkGenerationWorkBudget(Map, maxCellsPerFrame);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPosition = origin + new Vector2Int(x, y);
                if (TryEvaluateRiverCell(worldPosition, out float depth))
                    WriteFreshWaterAt(worldPosition, depth);

                if (!budget.ShouldYield())
                    continue;

                yield return null;
                budget.BeginNextFrame();
            }
        }
    }

    private bool TryPrepareGeneration(MapGenerationContext context, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (context?.Map == null)
        {
            LogNullContext(nameof(ChunkGenerator_River));
            return false;
        }

        Map = context.Map;
        activeWorldSeed = context.WorldSeed;
        targetTilemap ??= Map.tileMap;

        if (Map.Data == null)
        {
            Debug.LogError("[ChunkGenerator_River] Map.Data is null.", Map);
            return false;
        }

        if (riverTileBlock?.tileDataTemplate is not TileData_Water)
        {
            if (!hasLoggedInvalidTile)
            {
                hasLoggedInvalidTile = true;
                Debug.LogError("[ChunkGenerator_River] riverTileBlock must contain TileData_Water.", Map);
            }
            return false;
        }

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        width = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        height = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        Map.Data.EnsureTileDataArray(width, height, initCells: false);
        return true;
    }

    private int GetGenerationSeed()
    {
        return GetGenerationSeed(activeWorldSeed);
    }

    private int GetGenerationSeed(int worldSeed)
    {
        unchecked
        {
            return worldSeed * 486187739 ^ seed * 16777619;
        }
    }

    private void WriteFreshWaterAt(Vector2Int worldPosition, float depth)
    {
        List<TileData> tiles = Map.Data.GetTileListAt(worldPosition);
        TileData top = tiles != null && tiles.Count > 0 ? tiles[tiles.Count - 1] : null;
        if (top is TileData_Water existingWater && IsSeaWater(existingWater))
            return;

        TileData riverTile = riverTileBlock.tileDataTemplate.Clone();
        if (riverTile is not TileData_Water waterTile)
            return;

        riverTile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
        Vector2Int localPosition = worldPosition - Map.Data.position;
        EnvironmentLayers layers = Map.Data.EnvironmentLayers;
        if (layers != null && layers.Contains(localPosition.x, localPosition.y))
        {
            Map.Data.SetHumidityAtLocal(localPosition.x, localPosition.y, 1f);
            Map.Data.SetSolidityAtLocal(localPosition.x, localPosition.y, 0f);
            riverTile.Initialize_Env(layers, localPosition.x, localPosition.y);
        }

        waterTile.salt = 0f;
        waterTile.deepValue = Mathf.Clamp01(depth);

        if (tiles == null || tiles.Count == 0)
        {
            Map.Data.AddTileData(worldPosition, riverTile);
        }
        else if (writeMode == RiverWriteMode.AddLayer && top is not TileData_Water)
        {
            tiles.Add(riverTile);
        }
        else
        {
            tiles[tiles.Count - 1] = riverTile;
        }
    }

    private static bool IsSeaWater(TileData_Water water)
    {
        return Mathf.Abs(water.salt - SeaSalt) <= 0.01f;
    }
}
