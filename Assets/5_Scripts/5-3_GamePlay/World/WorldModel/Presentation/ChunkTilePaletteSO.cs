using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "ChunkTilePalette", menuName = "FlatWorld/World/Chunk Tile Palette")]
public sealed class ChunkTilePaletteSO : ScriptableObject
{
#pragma warning disable CS0649 // 这些字段由 Unity 序列化面板赋值。
    [Serializable]
    private struct Entry
    {
        public int TileId;
        public TileBase Tile;
    }
#pragma warning restore CS0649

    [SerializeField] private List<Entry> entries = new();
    private Dictionary<int, TileBase> lookup;

    public bool TryGetTile(int tileId, out TileBase tile)
    {
        // JSON 定义是运行时地块资源真源；MOD 新格子无需改写本体 Palette SO。
        if (GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(tileId, out var definition))
        {
            tile = definition.GetTileBaseAsset();
            return tile != null;
        }
        EnsureLookup();
        return lookup.TryGetValue(tileId, out tile) && tile != null;
    }

    /// <summary>读取 BRG 所需的正式 Sprite、颜色和 Tile 局部变换。</summary>
    public bool TryGetVisual(int tileId, out Sprite sprite, out Color color, out Matrix4x4 transform)
    {
        sprite = null;
        color = Color.white;
        transform = Matrix4x4.identity;
        if (!TryGetTile(tileId, out TileBase tileBase) || tileBase is not Tile tile || tile.sprite == null)
            return false;

        sprite = tile.sprite;
        color = tile.color;
        transform = tile.transform;
        return true;
    }

    #region 资源预热

    /// <summary>收集 Palette 最终映射；JSON/MOD 覆盖规则与实际 BRG 查询保持一致。</summary>
    public void CollectVisualSprites(ISet<Sprite> sprites)
    {
        if (sprites == null) throw new ArgumentNullException(nameof(sprites));
        EnsureLookup();
        foreach (int tileId in lookup.Keys)
            if (TryGetVisual(tileId, out Sprite sprite, out _, out _)) sprites.Add(sprite);
    }

    #endregion

    private void OnEnable() => lookup = null;
    private void OnValidate() => lookup = null;

    private void EnsureLookup()
    {
        if (lookup != null)
            return;
        lookup = new Dictionary<int, TileBase>();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.TileId == 0 || entry.Tile == null)
                continue;
            if (!lookup.TryAdd(entry.TileId, entry.Tile))
                Debug.LogError($"[ChunkTilePalette] 重复 TileId: {entry.TileId}", this);
        }
    }
}
