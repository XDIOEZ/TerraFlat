using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Profiling;

/// <summary>Pure presentation adapter for the model-owned grass layer.</summary>
public sealed class ChunkGrassRenderer : MonoBehaviour, IChunkViewRenderer
{
    private static readonly ProfilerMarker RenderAllMarker =
        new("FlatWorld.ChunkStreaming.RenderGrass");
    private const byte Present = 2;
    private const int CommonVariantCount = 24;

    [SerializeField] private Tilemap tilemap;
    [SerializeField] private Texture2D sourceTexture;
    [SerializeField, Min(1)] private int variantCount = 32;
    [SerializeField, Min(1)] private int textureColumns = 6;
    [SerializeField, Min(1)] private int spriteSizePixels = 16;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;
    [SerializeField, Range(0f, 0.45f)] private float positionJitter = 0.22f;
    [SerializeField] private Vector2 scaleRange = new(0.85f, 1.15f);
    [SerializeField, Range(0f, 1f)] private float accentVariantChance = 0.2f;

    private readonly List<Sprite> runtimeSprites = new();
    private readonly List<Tile> runtimeTiles = new();
    private ChunkRuntime boundChunk;
    private Texture2D boundTexture;

    public bool IsConfigured => tilemap != null && sourceTexture != null;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind grass rendering before data is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        EnsureRuntimeTiles();
        RenderAll(boundChunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (tilemap != null)
            tilemap.ClearAllTiles();
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind != TerrainChangeKind.Grass || boundChunk?.Terrain == null)
            return;
        RenderCell(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y);
    }

    private void RenderAll(ChunkTerrainData terrain)
    {
        if (tilemap == null || runtimeTiles.Count == 0 || terrain == null)
            return;

        using (RenderAllMarker.Auto())
        {
            tilemap.ClearAllTiles();
            var tiles = new TileBase[terrain.CellCount];
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (terrain.GetGrass(x, y) == Present)
                {
                    uint state = MixSeed(
                        boundChunk.Address.ChunkOrigin.X + x,
                        boundChunk.Address.ChunkOrigin.Y + y);
                    tiles[y * terrain.Width + x] = runtimeTiles[SelectVariant(ref state)];
                }
            }

            tilemap.SetTilesBlock(
                new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (terrain.GetGrass(x, y) == Present)
                    ApplyPresentCellVisual(x, y);
            }
        }
    }

    private void RenderCell(ChunkTerrainData terrain, int x, int y)
    {
        if (tilemap == null || runtimeTiles.Count == 0)
            return;
        if (terrain.GetGrass(x, y) == Present)
            RenderPresentCell(x, y);
        else
            tilemap.SetTile(new Vector3Int(x, y, 0), null);
    }

    private void RenderPresentCell(int x, int y)
    {
        int worldX = boundChunk.Address.ChunkOrigin.X + x;
        int worldY = boundChunk.Address.ChunkOrigin.Y + y;
        uint state = MixSeed(worldX, worldY);
        int variantIndex = SelectVariant(ref state);
        Vector3Int cell = new(x, y, 0);
        tilemap.SetTile(cell, runtimeTiles[variantIndex]);

        ApplyPresentCellVisual(cell, ref state);
    }

    /// <summary>重放确定性随机序列，并只应用矩阵与色调。</summary>
    private void ApplyPresentCellVisual(int x, int y)
    {
        int worldX = boundChunk.Address.ChunkOrigin.X + x;
        int worldY = boundChunk.Address.ChunkOrigin.Y + y;
        uint state = MixSeed(worldX, worldY);
        SelectVariant(ref state);
        ApplyPresentCellVisual(new Vector3Int(x, y, 0), ref state);
    }

    /// <summary>设置单格草地的偏移、缩放、翻转和色调。</summary>
    private void ApplyPresentCellVisual(Vector3Int cell, ref uint state)
    {

        float offsetX = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float offsetY = Mathf.Lerp(-positionJitter, positionJitter, Next01(ref state));
        float scale = Mathf.Lerp(
            Mathf.Min(scaleRange.x, scaleRange.y),
            Mathf.Max(scaleRange.x, scaleRange.y),
            Next01(ref state));
        float flipX = Next01(ref state) < 0.5f ? -1f : 1f;
        tilemap.SetTransformMatrix(cell, Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale * flipX, scale, 1f)));

        float tint = Mathf.Lerp(0.9f, 1f, Next01(ref state));
        tilemap.SetColor(cell, new Color(tint, tint, tint, 1f));
    }

    private int SelectVariant(ref uint state)
    {
        int count = runtimeTiles.Count;
        int commonCount = Mathf.Min(CommonVariantCount, count);
        if (count > commonCount && Next01(ref state) < accentVariantChance)
            return commonCount + (int)(Next(ref state) % (uint)(count - commonCount));
        return (int)(Next(ref state) % (uint)commonCount);
    }

    private void EnsureRuntimeTiles()
    {
        if (boundTexture == sourceTexture && runtimeTiles.Count > 0)
            return;

        DestroyRuntimeAssets();
        boundTexture = sourceTexture;
        if (sourceTexture == null)
            return;

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

            Sprite sprite = Sprite.Create(
                sourceTexture,
                new Rect(column * spriteSizePixels, y, spriteSizePixels, spriteSizePixels),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.Tight);
            sprite.name = $"ChunkGrassDetail_{index}";

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"ChunkGrassDetailTile_{index}";
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.None;
            runtimeSprites.Add(sprite);
            runtimeTiles.Add(tile);
        }
    }

    private void OnDestroy() => DestroyRuntimeAssets();

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

    private static float Next01(ref uint state) =>
        (Next(ref state) & 0xFFFFFFu) / (float)0x1000000;

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static uint MixSeed(int x, int y)
    {
        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)x) * 16777619u;
            state = (state ^ (uint)y) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }
}
