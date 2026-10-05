using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>最终内容校验后预构造地形 Sprite 网格；只依赖共享资源缓存，不创建 BRG 或绘制批次。</summary>
public partial class GameRes
{
    #region 世界生成资源与地形网格预热

    [SerializeField] private string[] worldAuthoringResourcePaths =
    {
        "Config/WorldModel/ChunkGenerationProfile_Surface",
        "Config/WorldModel/ChunkGenerationProfile_Cave",
        "Config/WorldModel/ChunkTilePalette_Default"
    };
    private readonly List<Object> worldAuthoringResources = new();

    /// <summary>校验器读取前先异步载入正式生成配置，避免同步 LoadAll 首次触发其 Unity 依赖加载。</summary>
    private IEnumerator LoadWorldAuthoringResources()
    {
        if (preparingInPlaceReload) yield break;
        foreach (string path in worldAuthoringResourcePaths)
        {
            ResourceRequest request = Resources.LoadAsync<Object>(path);
            yield return request;
            if (request.asset == null) throw new System.IO.InvalidDataException($"世界生成资源缺失：{path}");
            worldAuthoringResources.Add(request.asset);
            if (ShouldYieldResourceWork()) yield return null;
        }
    }

    /// <summary>收集最终目录和已加载 Palette，每份网格都遵守资源会话的统一帧预算。</summary>
    private IEnumerator PrewarmTerrainSpriteMeshes()
    {
        // 候选会话不能污染正式共享缓存；发布后的表现刷新通过 GetOrCreate 按需补齐新 Sprite。
        if (preparingInPlaceReload) yield break;
        var sprites = new HashSet<Sprite>();
        foreach (LiquidDefinition definition in LiquidDefinitions.Values)
            if (definition.WorldWater?.Sprite != null) sprites.Add(definition.WorldWater.Sprite);
        // 本体、JSON 引用与 MOD 动态登记都以合并后的运行时目录为准。
        foreach (TileBase tile in tileBaseDict.Values)
        {
            CollectTerrainSprite(tile, sprites);
            if (ShouldYieldResourceWork()) yield return null;
        }
        foreach (RuntimeTileDefinition definition in TileBlockDict.Values)
        {
            CollectTerrainSprite(definition?.GetTileBaseAsset(), sprites);
            if (ShouldYieldResourceWork()) yield return null;
        }

        // 显式异步加载正式 Palette，禁止用根目录 LoadAll 同步触发全部 Resources 的依赖加载。
        ResourceRequest paletteRequest = Resources.LoadAsync<ChunkTilePaletteSO>("Config/WorldModel/ChunkTilePalette_Default");
        yield return paletteRequest;
        var palettes = new HashSet<ChunkTilePaletteSO>();
        if (paletteRequest.asset is ChunkTilePaletteSO defaultPalette) palettes.Add(defaultPalette);
        foreach (ChunkTilePaletteSO palette in Resources.FindObjectsOfTypeAll<ChunkTilePaletteSO>())
            palettes.Add(palette);
        foreach (ChunkTilePaletteSO palette in palettes)
            if (palette != null) palette.CollectVisualSprites(sprites);
        palettes.Clear();

        int completed = 0;
        foreach (Sprite sprite in sprites)
        {
            SharedSpriteMeshCache.GetOrCreate(sprite);
            completed++;
            loadPipeline.Report((float)completed / sprites.Count);
            if (ShouldYieldResourceWork()) yield return null;
        }
        Debug.Log($"[GameRes] 地形 Sprite Mesh 预热完成：{sprites.Count} 个唯一 Sprite，缓存 {SharedSpriteMeshCache.Count} 个 Mesh。");
    }

    /// <summary>与 ChunkTilePaletteSO.TryGetVisual 相同，只收集实际支持 BRG 的 Tile Sprite。</summary>
    private static void CollectTerrainSprite(TileBase tileBase, ISet<Sprite> sprites)
    {
        if (tileBase is Tile tile && tile != null && tile.sprite != null) sprites.Add(tile.sprite);
    }

    #endregion
}
