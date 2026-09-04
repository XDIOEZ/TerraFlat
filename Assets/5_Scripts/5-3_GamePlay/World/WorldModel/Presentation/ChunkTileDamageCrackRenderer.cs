using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 把阻挡墙格的权威累计损伤表现为像素裂缝。
/// 组件只监听区块地形变更，不持有第二份生命值；裂缝按四档从 0.45 倍扩大到 1.05 倍，
/// 区块重新绑定时会直接从持久化损伤层重建，并复用每个受损格的 SpriteRenderer。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ChunkTilemapRenderer))]
public sealed class ChunkTileDamageCrackRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置

    [SerializeField] private Sprite crackSprite;
    [SerializeField, Range(2, 8)] private int damageStageCount = 4;
    [SerializeField, Range(0.1f, 1f)] private float minimumScale = 0.45f;
    [SerializeField, Range(0.5f, 1.5f)] private float maximumScale = 1.05f;
    [SerializeField, Range(0f, 1f)] private float minimumAlpha = 0.68f;
    [SerializeField, Min(0)] private int sortingOrderOffset = 1;

    #endregion

    #region 运行时状态

    private readonly Dictionary<Vector2Int, SpriteRenderer> activeCracks = new();
    private readonly Stack<SpriteRenderer> crackPool = new();
    private ChunkTilemapRenderer tilemapRenderer;
    private ChunkRuntime boundChunk;

    #endregion

    #region 绑定与生命周期

    private void Awake()
    {
        tilemapRenderer = GetComponent<ChunkTilemapRenderer>();
    }

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new InvalidOperationException("Cannot bind wall damage rendering before terrain is ready.");
        if (crackSprite == null)
            throw new MissingReferenceException("[ChunkTileDamageCrackRenderer] ChunkView 缺少裂缝 Sprite。");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        tilemapRenderer ??= GetComponent<ChunkTilemapRenderer>();
        if (tilemapRenderer.BlockingTilemapRenderer == null)
        {
            throw new MissingComponentException(
                "[ChunkTileDamageCrackRenderer] ChunkView 缺少阻挡层 TilemapRenderer。");
        }

        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        RefreshAllCells();
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;

        foreach (SpriteRenderer renderer in activeCracks.Values)
            Recycle(renderer);
        activeCracks.Clear();
        boundChunk = null;
    }

    private void OnDisable() => Unbind();

    #endregion

    #region 裂缝刷新

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind != TerrainChangeKind.Cell &&
            changed.Kind != TerrainChangeKind.TileStack &&
            changed.Kind != TerrainChangeKind.Environment)
        {
            return;
        }

        RefreshCell(new Vector2Int(changed.LocalCell.X, changed.LocalCell.Y));
    }

    /// <summary>区块载入或读档后按权威损伤层恢复全部裂缝。</summary>
    private void RefreshAllCells()
    {
        ChunkTerrainData terrain = boundChunk?.Terrain;
        if (terrain == null)
            return;

        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
                RefreshCell(new Vector2Int(x, y));
        }
    }

    private void RefreshCell(Vector2Int localCell)
    {
        if (!TileBuildingSystem.TryGetRuntimeDamageFraction(
                boundChunk,
                localCell,
                out float damageFraction) ||
            damageFraction <= 0f)
        {
            HideCrack(localCell);
            return;
        }

        SpriteRenderer renderer = GetOrCreateCrack(localCell);
        int stageCount = Mathf.Max(2, damageStageCount);
        int stage = Mathf.Clamp(Mathf.CeilToInt(damageFraction * stageCount), 1, stageCount);
        float stageProgress = (stage - 1f) / (stageCount - 1f);
        float scale = Mathf.Lerp(minimumScale, maximumScale, stageProgress);
        float alpha = Mathf.Lerp(minimumAlpha, 1f, stageProgress);

        renderer.transform.localPosition = new Vector3(localCell.x + 0.5f, localCell.y + 0.5f, 0f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, ResolveRotation(localCell));
        renderer.transform.localScale = Vector3.one * scale;
        renderer.color = new Color(1f, 1f, 1f, alpha);
        renderer.gameObject.SetActive(true);
    }

    private SpriteRenderer GetOrCreateCrack(Vector2Int localCell)
    {
        if (activeCracks.TryGetValue(localCell, out SpriteRenderer existing))
            return existing;

        SpriteRenderer renderer;
        if (crackPool.Count > 0)
        {
            renderer = crackPool.Pop();
        }
        else
        {
            var crackObject = new GameObject("WallDamageCrack");
            crackObject.transform.SetParent(transform, false);
            renderer = crackObject.AddComponent<SpriteRenderer>();
            renderer.sprite = crackSprite;
            renderer.spriteSortPoint = SpriteSortPoint.Center;
        }

        ConfigureRenderer(renderer);
        activeCracks.Add(localCell, renderer);
        return renderer;
    }

    private void ConfigureRenderer(SpriteRenderer renderer)
    {
        TilemapRenderer blockingRenderer = tilemapRenderer.BlockingTilemapRenderer;
        renderer.sprite = crackSprite;
        renderer.sharedMaterial = blockingRenderer.sharedMaterial;
        renderer.sortingLayerID = blockingRenderer.sortingLayerID;
        renderer.sortingOrder = blockingRenderer.sortingOrder + sortingOrderOffset;
    }

    private void HideCrack(Vector2Int localCell)
    {
        if (!activeCracks.Remove(localCell, out SpriteRenderer renderer))
            return;

        Recycle(renderer);
    }

    private void Recycle(SpriteRenderer renderer)
    {
        if (renderer == null)
            return;

        renderer.gameObject.SetActive(false);
        crackPool.Push(renderer);
    }

    /// <summary>按世界格坐标稳定旋转裂缝，避免连续墙格出现完全相同的纹理。</summary>
    private float ResolveRotation(Vector2Int localCell)
    {
        Int2 origin = boundChunk.Address.ChunkOrigin;
        int worldX = origin.X + localCell.x;
        int worldY = origin.Y + localCell.y;
        int hash = unchecked(worldX * 73856093 ^ worldY * 19349663);
        return (hash & 3) * 90f;
    }

    #endregion
}
