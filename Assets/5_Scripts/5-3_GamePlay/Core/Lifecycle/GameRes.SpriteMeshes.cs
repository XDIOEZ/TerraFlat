using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>最终内容校验后预构造地形 Sprite 网格；只依赖共享资源缓存，不创建 BRG 或绘制批次。</summary>
public partial class GameRes
{
    #region 地形网格预热

    /// <summary>收集最终目录和 Palette，按唯一 Sprite 每批最多 32 份网格让出一帧。</summary>
    private IEnumerator PrewarmTerrainSpriteMeshes()
    {
        // 候选会话不能污染正式共享缓存；发布后的表现刷新通过 GetOrCreate 按需补齐新 Sprite。
        if (preparingInPlaceReload) yield break;
        const int meshesPerFrame = 32;
        var sprites = new HashSet<Sprite>();
        foreach (LiquidDefinition definition in LiquidDefinitions.Values)
            if (definition.WorldWater?.Sprite != null) sprites.Add(definition.WorldWater.Sprite);
        // 本体、JSON 引用与 MOD 动态登记都以合并后的运行时目录为准。
        foreach (TileBase tile in tileBaseDict.Values) CollectTerrainSprite(tile, sprites);
        foreach (RuntimeTileDefinition definition in TileBlockDict.Values)
            CollectTerrainSprite(definition?.GetTileBaseAsset(), sprites);

        // Resources Palette 可能尚未随 ChunkView 加载；再合并已加载的 Addressables/MOD Palette。
        var palettes = new HashSet<ChunkTilePaletteSO>(Resources.LoadAll<ChunkTilePaletteSO>(string.Empty));
        foreach (ChunkTilePaletteSO palette in Resources.FindObjectsOfTypeAll<ChunkTilePaletteSO>())
            palettes.Add(palette);
        foreach (ChunkTilePaletteSO palette in palettes)
            if (palette != null) palette.CollectVisualSprites(sprites);
        palettes.Clear();

        int completed = 0;
        var batch = new List<Sprite>(meshesPerFrame);
        foreach (Sprite sprite in sprites)
        {
            batch.Add(sprite);
            if (batch.Count < meshesPerFrame) continue;
            SharedSpriteMeshCache.Prewarm(batch);
            completed += batch.Count;
            batch.Clear();
            loadPipeline.Report((float)completed / sprites.Count);
            yield return null;
        }
        SharedSpriteMeshCache.Prewarm(batch);
        Debug.Log($"[GameRes] 地形 Sprite Mesh 预热完成：{sprites.Count} 个唯一 Sprite，缓存 {SharedSpriteMeshCache.Count} 个 Mesh。");
    }

    /// <summary>与 ChunkTilePaletteSO.TryGetVisual 相同，只收集实际支持 BRG 的 Tile Sprite。</summary>
    private static void CollectTerrainSprite(TileBase tileBase, ISet<Sprite> sprites)
    {
        if (tileBase is Tile tile && tile != null && tile.sprite != null) sprites.Add(tile.sprite);
    }

    #endregion
}
