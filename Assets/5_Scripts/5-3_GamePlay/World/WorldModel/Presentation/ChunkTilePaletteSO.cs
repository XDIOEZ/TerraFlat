using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "ChunkTilePalette", menuName = "FlatWorld/World/Chunk Tile Palette")]
public sealed class ChunkTilePaletteSO : ScriptableObject
{
    [Serializable]
    private struct Entry
    {
        public int TileId;
        public TileBase Tile;
    }

    [SerializeField] private List<Entry> entries = new();
    private Dictionary<int, TileBase> lookup;

    public bool TryGetTile(int tileId, out TileBase tile)
    {
        EnsureLookup();
        return lookup.TryGetValue(tileId, out tile) && tile != null;
    }

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
