using MemoryPack;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial struct TileStackCell
{
    public TileData BaseTile;
    public TileData OverlayTile;
    public List<TileData> OverflowLayers;

    [MemoryPackIgnore]
    public readonly int Count =>
        (BaseTile == null ? 0 : 1) +
        (OverlayTile == null ? 0 : 1) +
        (OverflowLayers?.Count ?? 0);

    [MemoryPackIgnore]
    public readonly bool IsEmpty => BaseTile == null;

    [MemoryPackIgnore]
    public readonly bool HasOverflowAllocation => OverflowLayers != null;

    public readonly TileData GetAt(int index)
    {
        if (index < 0)
            return null;

        if (index == 0)
            return BaseTile;

        if (index == 1)
            return OverlayTile;

        int overflowIndex = index - 2;
        return OverflowLayers != null && (uint)overflowIndex < (uint)OverflowLayers.Count
            ? OverflowLayers[overflowIndex]
            : null;
    }

    public readonly TileData GetFromTop(int offset = 0)
    {
        int index = Count - 1 - offset;
        return index >= 0 ? GetAt(index) : null;
    }

    public void SetBase(TileData tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));

        BaseTile = tile;
        Normalize();
    }

    public void Push(TileData tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));

        Normalize();
        if (BaseTile == null)
        {
            BaseTile = tile;
            return;
        }

        if (OverlayTile == null)
        {
            OverlayTile = tile;
            return;
        }

        OverflowLayers ??= new List<TileData>(2);
        OverflowLayers.Add(tile);
    }

    public bool ReplaceTop(TileData tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));

        Normalize();
        int count = Count;
        if (count == 0)
        {
            BaseTile = tile;
            return false;
        }

        return ReplaceAt(count - 1, tile);
    }

    public bool ReplaceAt(int index, TileData tile)
    {
        if (tile == null)
            throw new ArgumentNullException(nameof(tile));

        Normalize();
        if (index == 0 && BaseTile != null)
        {
            BaseTile = tile;
            return true;
        }

        if (index == 1 && OverlayTile != null)
        {
            OverlayTile = tile;
            return true;
        }

        int overflowIndex = index - 2;
        if (OverflowLayers == null || (uint)overflowIndex >= (uint)OverflowLayers.Count)
            return false;

        OverflowLayers[overflowIndex] = tile;
        return true;
    }

    public bool RemoveAt(int index)
    {
        Normalize();
        int count = Count;
        if ((uint)index >= (uint)count)
            return false;

        if (index == 0)
        {
            BaseTile = OverlayTile;
            if (OverflowLayers != null && OverflowLayers.Count > 0)
            {
                OverlayTile = OverflowLayers[0];
                OverflowLayers.RemoveAt(0);
            }
            else
            {
                OverlayTile = null;
            }
        }
        else if (index == 1)
        {
            if (OverflowLayers != null && OverflowLayers.Count > 0)
            {
                OverlayTile = OverflowLayers[0];
                OverflowLayers.RemoveAt(0);
            }
            else
            {
                OverlayTile = null;
            }
        }
        else
        {
            OverflowLayers.RemoveAt(index - 2);
        }

        if (OverflowLayers != null && OverflowLayers.Count == 0)
            OverflowLayers = null;

        Normalize();
        return true;
    }

    public void Clear()
    {
        BaseTile = null;
        OverlayTile = null;
        OverflowLayers = null;
    }

    public void ReplaceWith(IReadOnlyList<TileData> tiles)
    {
        Clear();
        if (tiles == null)
            return;

        for (int i = 0; i < tiles.Count; i++)
        {
            TileData tile = tiles[i];
            if (tile != null)
                Push(tile);
        }
    }

    public readonly TileStackView AsView()
    {
        return new TileStackView(BaseTile, OverlayTile, OverflowLayers);
    }

    public readonly void CopyTo(List<TileData> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        if (BaseTile != null)
            destination.Add(BaseTile);
        if (OverlayTile != null)
            destination.Add(OverlayTile);
        if (OverflowLayers != null)
            destination.AddRange(OverflowLayers);
    }

    private void Normalize()
    {
        if (BaseTile == null)
        {
            BaseTile = OverlayTile;
            OverlayTile = null;
        }

        if (BaseTile != null && OverlayTile == null && OverflowLayers != null && OverflowLayers.Count > 0)
        {
            OverlayTile = OverflowLayers[0];
            OverflowLayers.RemoveAt(0);
        }

        if (OverflowLayers == null)
            return;

        for (int i = OverflowLayers.Count - 1; i >= 0; i--)
        {
            if (OverflowLayers[i] == null)
                OverflowLayers.RemoveAt(i);
        }

        if (OverflowLayers.Count == 0)
            OverflowLayers = null;
    }
}

public readonly struct TileStackView : IReadOnlyList<TileData>
{
    private readonly TileData _baseTile;
    private readonly TileData _overlayTile;
    private readonly List<TileData> _overflowLayers;

    public TileStackView(TileData baseTile, TileData overlayTile, List<TileData> overflowLayers)
    {
        _baseTile = baseTile;
        _overlayTile = overlayTile;
        _overflowLayers = overflowLayers;
    }

    public int Count =>
        (_baseTile == null ? 0 : 1) +
        (_overlayTile == null ? 0 : 1) +
        (_overflowLayers?.Count ?? 0);

    public TileData this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (index == 0)
                return _baseTile;
            if (index == 1)
                return _overlayTile;
            return _overflowLayers[index - 2];
        }
    }

    public TileData GetFromTop(int offset = 0)
    {
        int index = Count - 1 - offset;
        return index >= 0 ? this[index] : null;
    }

    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<TileData> IEnumerable<TileData>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<TileData>
    {
        private readonly TileStackView _view;
        private int _index;

        internal Enumerator(TileStackView view)
        {
            _view = view;
            _index = -1;
        }

        public TileData Current => _view[_index];
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _index++;
            return _index < _view.Count;
        }

        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

public readonly struct OccupiedTileCell
{
    public Vector2Int WorldPosition { get; }
    public TileStackView Stack { get; }

    public OccupiedTileCell(Vector2Int worldPosition, TileStackView stack)
    {
        WorldPosition = worldPosition;
        Stack = stack;
    }

    public void Deconstruct(out Vector2Int worldPosition, out TileStackView stack)
    {
        worldPosition = WorldPosition;
        stack = Stack;
    }
}
