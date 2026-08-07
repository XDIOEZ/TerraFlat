using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 草地的纯视觉细节层。使用 Tilemap 批处理，不创建 Item、碰撞体或存档数据。
/// </summary>
[DisallowMultipleComponent]
public sealed class GrassDetailLayer : MonoBehaviour
{
    private const string DetailObjectName = "GrassDetails";
    private const int CommonVariantCount = 24;

    [Header("草地资源")]
    [SerializeField] private Texture2D sourceTexture;
    [SerializeField] private string grassTileId = "Tile_Grass";
    [SerializeField, Min(1)] private int variantCount = 32;
    [SerializeField, Min(1)] private int textureColumns = 6;
    [SerializeField, Min(1)] private int spriteSizePixels = 16;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;

    [Header("分布")]
    [SerializeField, Range(0f, 0.8f)] private float density = 0.22f;
    [SerializeField] private bool varyDensityWithPrecipitation = true;
    [SerializeField, Range(0f, 0.45f)] private float positionJitter = 0.22f;
    [SerializeField] private Vector2 scaleRange = new(0.85f, 1.15f);
    [SerializeField, Range(0f, 1f)] private float accentVariantChance = 0.2f;

    [Header("渲染")]
    [SerializeField] private int sortingOrderOffset = 1;

    private readonly List<Sprite> runtimeSprites = new();
    private readonly List<Tile> runtimeTiles = new();
    private Tilemap detailTilemap;
    private Texture2D boundTexture;

    public void Rebuild(Map map)
    {
        if (map == null || map.Data == null || sourceTexture == null)
            return;

        map.Data.EnsureGrassLayerStorage(map.Data.Width, map.Data.Height);
        EnsureDetailTilemap(map);
        EnsureRuntimeTiles();
        if (detailTilemap == null || runtimeTiles.Count == 0)
            return;

        detailTilemap.ClearAllTiles();
        int worldSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;

        foreach (var (worldPosition, tileDataList) in map.Data.EnumerateOccupiedCells())
        {
            if (tileDataList.Count == 0)
                continue;

            ApplyCell(map, worldPosition, tileDataList[^1], worldSeed);
        }
    }

    public void RefreshCell(Map map, Vector2Int worldPosition)
    {
        if (map == null || map.Data == null || sourceTexture == null)
            return;

        map.Data.EnsureGrassLayerStorage(map.Data.Width, map.Data.Height);
        EnsureDetailTilemap(map);
        EnsureRuntimeTiles();
        if (detailTilemap == null || runtimeTiles.Count == 0)
            return;

        TileData topTile = map.Data.GetTopTile(worldPosition);
        int worldSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        ApplyCell(map, worldPosition, topTile, worldSeed);
    }

    public bool RemoveGrassAt(Map map, Vector2Int worldPosition)
    {
        if (!HasGrassAt(map, worldPosition))
            return false;

        if (!map.Data.TrySetGrassStateAtWorld(worldPosition, GrassCellState.Removed))
            return false;

        EnsureDetailTilemap(map);
        detailTilemap?.SetTile(new Vector3Int(worldPosition.x, worldPosition.y, 0), null);
        return true;
    }

    public bool HasGrassAt(Map map, Vector2Int worldPosition)
    {
        if (map == null || map.Data == null)
            return false;

        TileData topTile = map.Data.GetTopTile(worldPosition);
        return topTile != null &&
               topTile.ID == grassTileId &&
               map.Data.TryGetGrassStateAtWorld(worldPosition, out GrassCellState state) &&
               state == GrassCellState.Present;
    }

    public bool TryFindClosestGrass(
        Map map,
        Vector2 worldPosition,
        float searchRadius,
        out Vector2Int grassPosition)
    {
        grassPosition = default;
        if (map == null || map.Data == null || searchRadius < 0f)
            return false;

        int width = map.Data.Width;
        int height = map.Data.Height;
        if (width <= 0 || height <= 0)
            return false;

        Vector2Int mapOrigin = map.Data.position;
        int minX = Mathf.Max(mapOrigin.x, Mathf.FloorToInt(worldPosition.x - searchRadius));
        int maxX = Mathf.Min(mapOrigin.x + width - 1, Mathf.FloorToInt(worldPosition.x + searchRadius));
        int minY = Mathf.Max(mapOrigin.y, Mathf.FloorToInt(worldPosition.y - searchRadius));
        int maxY = Mathf.Min(mapOrigin.y + height - 1, Mathf.FloorToInt(worldPosition.y + searchRadius));
        float radiusSqr = searchRadius * searchRadius;
        float closestDistanceSqr = float.MaxValue;
        bool found = false;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int candidate = new(x, y);
                Vector2 candidateCenter = new(x + 0.5f, y + 0.5f);
                float distanceSqr = (candidateCenter - worldPosition).sqrMagnitude;
                if (distanceSqr > radiusSqr || distanceSqr >= closestDistanceSqr)
                    continue;

                if (!HasGrassAt(map, candidate))
                    continue;

                grassPosition = candidate;
                closestDistanceSqr = distanceSqr;
                found = true;
            }
        }

        return found;
    }

    private void ApplyCell(Map map, Vector2Int worldPosition, TileData topTile, int worldSeed)
    {
        Vector3Int cell = new(worldPosition.x, worldPosition.y, 0);
        if (topTile == null || topTile.ID != grassTileId)
        {
            detailTilemap.SetTile(cell, null);
            return;
        }

        uint state = MixSeed(worldPosition.x, worldPosition.y, worldSeed);
        float localDensity = GetLocalDensity(map, worldPosition);
        bool generatedWithGrass = Next01(ref state) < localDensity;

        if (!map.Data.TryGetGrassStateAtWorld(worldPosition, out GrassCellState grassState))
        {
            detailTilemap.SetTile(cell, null);
            return;
        }

        if (grassState == GrassCellState.Uninitialized)
        {
            grassState = generatedWithGrass ? GrassCellState.Present : GrassCellState.Empty;
            map.Data.TrySetGrassStateAtWorld(worldPosition, grassState);
        }

        if (grassState != GrassCellState.Present)
        {
            detailTilemap.SetTile(cell, null);
            return;
        }

        int variantIndex = SelectVariant(ref state);
        detailTilemap.SetTile(cell, runtimeTiles[variantIndex]);

        float offsetX = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float offsetY = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float scale = Mathf.Lerp(
            Mathf.Min(scaleRange.x, scaleRange.y),
            Mathf.Max(scaleRange.x, scaleRange.y),
            Next01(ref state));
        float flipX = Next01(ref state) < 0.5f ? -1f : 1f;
        Matrix4x4 matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale * flipX, scale, 1f));
        detailTilemap.SetTransformMatrix(cell, matrix);

        float tint = Mathf.Lerp(0.9f, 1f, Next01(ref state));
        detailTilemap.SetColor(cell, new Color(tint, tint, tint, 1f));
    }

    private float GetLocalDensity(Map map, Vector2Int worldPosition)
    {
        float result = density;
        if (!varyDensityWithPrecipitation ||
            !map.Data.TryGetEnvironmentLocalPos(worldPosition, out Vector2Int localPosition))
            return result;

        float precipitation = map.Data.EnvironmentLayers.Precipitation[localPosition.x, localPosition.y];
        return Mathf.Clamp01(result * Mathf.Lerp(0.7f, 1.25f, Mathf.Clamp01(precipitation)));
    }

    private int SelectVariant(ref uint state)
    {
        int count = runtimeTiles.Count;
        int commonCount = Mathf.Min(CommonVariantCount, count);
        if (count > commonCount && Next01(ref state) < accentVariantChance)
            return commonCount + (int)(Next(ref state) % (uint)(count - commonCount));

        return (int)(Next(ref state) % (uint)commonCount);
    }

    private void EnsureDetailTilemap(Map map)
    {
        if (detailTilemap != null)
            return;

        Transform existing = transform.Find(DetailObjectName);
        GameObject detailObject;
        if (existing != null)
        {
            detailObject = existing.gameObject;
        }
        else
        {
            detailObject = new GameObject(DetailObjectName);
            detailObject.layer = gameObject.layer;
            detailObject.transform.SetParent(transform, false);
        }

        detailTilemap = detailObject.GetComponent<Tilemap>();
        if (detailTilemap == null)
            detailTilemap = detailObject.AddComponent<Tilemap>();

        TilemapRenderer detailRenderer = detailObject.GetComponent<TilemapRenderer>();
        if (detailRenderer == null)
            detailRenderer = detailObject.AddComponent<TilemapRenderer>();

        TilemapRenderer groundRenderer = map.tileMap != null ? map.tileMap.GetComponent<TilemapRenderer>() : null;
        if (groundRenderer != null)
        {
            detailRenderer.sortingLayerID = groundRenderer.sortingLayerID;
            detailRenderer.sortingOrder = groundRenderer.sortingOrder + sortingOrderOffset;
            detailRenderer.sharedMaterial = groundRenderer.sharedMaterial;
        }
    }

    private void EnsureRuntimeTiles()
    {
        if (boundTexture == sourceTexture && runtimeTiles.Count > 0)
            return;

        DestroyRuntimeAssets();
        boundTexture = sourceTexture;

        int rows = sourceTexture.height / spriteSizePixels;
        int columns = Mathf.Min(textureColumns, sourceTexture.width / spriteSizePixels);
        int availableCount = Mathf.Min(variantCount, rows * columns);
        for (int index = 0; index < availableCount; index++)
        {
            int column = index % columns;
            int rowFromTop = index / columns;
            int y = sourceTexture.height - (rowFromTop + 1) * spriteSizePixels;
            if (y < 0)
                break;

            Rect rect = new(
                column * spriteSizePixels,
                y,
                spriteSizePixels,
                spriteSizePixels);
            Sprite sprite = Sprite.Create(
                sourceTexture,
                rect,
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.Tight);
            sprite.name = $"GrassDetail_{index}";

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"GrassDetailTile_{index}";
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.None;

            runtimeSprites.Add(sprite);
            runtimeTiles.Add(tile);
        }
    }

    private void OnDestroy()
    {
        DestroyRuntimeAssets();
    }

    private void DestroyRuntimeAssets()
    {
        for (int i = 0; i < runtimeTiles.Count; i++)
            DestroyRuntimeAsset(runtimeTiles[i]);
        for (int i = 0; i < runtimeSprites.Count; i++)
            DestroyRuntimeAsset(runtimeSprites[i]);

        runtimeTiles.Clear();
        runtimeSprites.Clear();
        boundTexture = null;
    }

    private static void DestroyRuntimeAsset(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private static float Next01(ref uint state)
        => (Next(ref state) & 0xFFFFFFu) / (float)0x1000000;

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static uint MixSeed(int x, int y, int worldSeed)
    {
        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)x) * 16777619u;
            state = (state ^ (uint)y) * 16777619u;
            state = (state ^ (uint)worldSeed) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }
}
