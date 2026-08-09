using MemoryPack;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [NonSerialized]
    private bool _hasLoggedStorageNotInitialized;

    [NonSerialized]
    private int _nonEmptyCellCount = -1;

    [MemoryPackInclude]
    [SerializeField, HideInInspector]
    private TileStackCell[,] _tileCells = new TileStackCell[20, 20];

    [Tooltip("地图的位置")]
    public Vector2Int position = Vector2Int.zero;

    public bool TileLoaded;

    [Tooltip("环境多层网格（温度、摄氏温度、降水、高度、风向与光照）")]
    public EnvironmentLayers EnvironmentLayers = new EnvironmentLayers();

    [Tooltip("装饰草层数据，每格使用一个字节保存状态")]
    public GrassLayerData GrassLayer = new GrassLayerData();

    [MemoryPackIgnore]
    public int Width => _tileCells != null && _tileCells.Length > 0 ? _tileCells.GetLength(0) : 0;

    [MemoryPackIgnore]
    public int Height => _tileCells != null && _tileCells.Length > 0 ? _tileCells.GetLength(1) : 0;

    public void EnsureTileStorage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"[Data_TileMap] 地形尺寸非法：{width}x{height}");

        if (_tileCells == null || _tileCells.GetLength(0) != width || _tileCells.GetLength(1) != height)
        {
            _tileCells = new TileStackCell[width, height];
            _nonEmptyCellCount = 0;
        }

        EnsureGrassLayerStorage(width, height);
    }

    public void ClearAllTiles()
    {
        if (_tileCells != null && _tileCells.Length > 0)
            Array.Clear(_tileCells, 0, _tileCells.Length);

        _nonEmptyCellCount = 0;
        GrassLayer?.Clear();
    }

    #region Grass Layer

    public void EnsureGrassLayerStorage(int width, int height)
    {
        GrassLayer ??= new GrassLayerData();
        GrassLayer.EnsureSize(width, height);
    }

    public bool TryGetGrassStateAtWorld(Vector2Int worldPos, out GrassCellState state)
    {
        Vector2Int localPos = worldPos - position;
        EnsureGrassLayerStorage(Width, Height);
        if (!GrassLayer.Contains(localPos.x, localPos.y))
        {
            state = GrassCellState.Uninitialized;
            return false;
        }

        state = GrassLayer.Get(localPos.x, localPos.y);
        return true;
    }

    public bool TrySetGrassStateAtWorld(Vector2Int worldPos, GrassCellState state)
    {
        Vector2Int localPos = worldPos - position;
        EnsureGrassLayerStorage(Width, Height);
        return GrassLayer.Set(localPos.x, localPos.y, state);
    }

    /// <summary>从草层消费一格草，只有存在的草才能成功消费。</summary>
    public bool TryConsumeGrassAtWorld(Vector2Int worldPos)
    {
        Vector2Int localPos = worldPos - position;
        EnsureGrassLayerStorage(Width, Height);
        return GrassLayer.TryConsume(localPos.x, localPos.y);
    }

    #endregion

    #region Environment Layers

    public void EnsureEnvironmentStorage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"[Data_TileMap] 环境尺寸非法：{width}x{height}");

        EnvironmentLayers ??= new EnvironmentLayers();
        EnvironmentLayers.EnsureSize(width, height);
    }

    public bool IsEnvironmentLocalValid(int x, int y)
    {
        return EnvironmentLayers != null && EnvironmentLayers.Contains(x, y);
    }

    public bool TryGetEnvironmentLocalPos(Vector2Int worldPos, out Vector2Int localPos)
    {
        localPos = worldPos - position;
        return IsEnvironmentLocalValid(localPos.x, localPos.y);
    }

    public void SetEnvironmentAtLocal(
        int x,
        int y,
        float temperature,
        float temperatureCelsius,
        float precipitation,
        float height)
    {
        ValidateEnvironmentCell(x, y);
        EnvironmentLayers.SetCell(x, y, temperature, temperatureCelsius, precipitation, height);
    }

    public void SetPrecipitationAtLocal(int x, int y, float precipitation)
    {
        ValidateEnvironmentCell(x, y);
        EnvironmentLayers.SetPrecipitation(x, y, precipitation);
    }

    public void SetWindAtLocal(int x, int y, Vector2 direction)
    {
        ValidateEnvironmentCell(x, y);
        EnvironmentLayers.SetWind(x, y, direction);
    }

    public void SetLightAtLocal(int x, int y, float light)
    {
        ValidateEnvironmentCell(x, y);
        EnvironmentLayers.SetLight(x, y, light);
    }

    public bool TryGetLightAtWorld(Vector2 worldPos, out float light)
    {
        Vector2Int worldCell = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
        if (!TryGetEnvironmentLocalPos(worldCell, out Vector2Int localPos))
        {
            light = 0f;
            return false;
        }

        light = EnvironmentLayers.GetLight(localPos.x, localPos.y);
        return true;
    }

    private void ValidateEnvironmentCell(int x, int y)
    {
        if (!IsEnvironmentLocalValid(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"[Data_TileMap] 环境坐标越界：({x},{y}) size={EnvironmentLayers?.Width}x{EnvironmentLayers?.GridHeight}");
    }

    #endregion

    #region Tile Stack Queries

    public int GetLayerCount(Vector2Int worldPos)
    {
        return TryGetCellIndex(worldPos, out int x, out int y) ? _tileCells[x, y].Count : 0;
    }

    public TileData GetTileAt(Vector2Int worldPos, int index)
    {
        return TryGetCellIndex(worldPos, out int x, out int y) ? _tileCells[x, y].GetAt(index) : null;
    }

    public TileData GetTileFromTop(Vector2Int worldPos, int offset = 0)
    {
        return TryGetCellIndex(worldPos, out int x, out int y) ? _tileCells[x, y].GetFromTop(offset) : null;
    }

    public TileData GetTopTile(Vector2Int worldPos)
    {
        return GetTileFromTop(worldPos);
    }

    public bool TryGetStackView(Vector2Int worldPos, out TileStackView stack)
    {
        if (!TryGetCellIndex(worldPos, out int x, out int y))
        {
            stack = default;
            return false;
        }

        stack = _tileCells[x, y].AsView();
        return true;
    }

    public bool TryGetStackViewLocal(int x, int y, out TileStackView stack)
    {
        if (!IsLocalCellValid(x, y))
        {
            stack = default;
            return false;
        }

        stack = _tileCells[x, y].AsView();
        return true;
    }

    public bool CopyStackTo(Vector2Int worldPos, List<TileData> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        if (!TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].CopyTo(destination);
        return destination.Count > 0;
    }

    public bool CopyStackLocalTo(int x, int y, List<TileData> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        if (!IsLocalCellValid(x, y))
            return false;

        _tileCells[x, y].CopyTo(destination);
        return destination.Count > 0;
    }

    #endregion

    #region Tile Stack Mutations

    public bool SetBaseTile(Vector2Int worldPos, TileData tile)
    {
        if (tile == null || !TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].SetBase(tile);
        InvalidateNonEmptyCount();
        return true;
    }

    public bool PushTile(Vector2Int worldPos, TileData tile)
    {
        if (tile == null || !TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].Push(tile);
        InvalidateNonEmptyCount();
        return true;
    }

    public bool ReplaceTop(Vector2Int worldPos, TileData tile)
    {
        if (tile == null || !TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].ReplaceTop(tile);
        InvalidateNonEmptyCount();
        return true;
    }

    public bool UpdateTileAt(Vector2Int worldPos, int index, TileData tile)
    {
        if (tile == null || !TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        bool updated = _tileCells[x, y].ReplaceAt(index, tile);
        if (updated)
            InvalidateNonEmptyCount();
        return updated;
    }

    public bool RemoveTile(Vector2Int worldPos, int? index = null)
    {
        if (!TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        int resolvedIndex = index ?? (_tileCells[x, y].Count - 1);
        bool removed = _tileCells[x, y].RemoveAt(resolvedIndex);
        if (removed)
            InvalidateNonEmptyCount();
        return removed;
    }

    public bool ClearCell(Vector2Int worldPos)
    {
        if (!TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        bool changed = !_tileCells[x, y].IsEmpty;
        _tileCells[x, y].Clear();
        if (changed)
            InvalidateNonEmptyCount();
        return changed;
    }

    public bool ReplaceStack(Vector2Int worldPos, IReadOnlyList<TileData> tiles)
    {
        if (!TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].ReplaceWith(tiles);
        InvalidateNonEmptyCount();
        return true;
    }

    public bool ReplaceStack(Vector2Int worldPos, TileData tile)
    {
        if (tile == null || !TryGetCellIndex(worldPos, out int x, out int y))
            return false;

        _tileCells[x, y].Clear();
        _tileCells[x, y].SetBase(tile);
        InvalidateNonEmptyCount();
        return true;
    }

    public bool ReplaceStackLocal(int x, int y, IReadOnlyList<TileData> tiles)
    {
        if (!IsLocalCellValid(x, y))
            return false;

        _tileCells[x, y].ReplaceWith(tiles);
        InvalidateNonEmptyCount();
        return true;
    }

    #endregion

    public IEnumerable<OccupiedTileCell> EnumerateOccupiedCells()
    {
        if (_tileCells == null || _tileCells.Length == 0)
            yield break;

        int width = Width;
        int height = Height;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileStackCell cell = _tileCells[x, y];
                if (cell.IsEmpty)
                    continue;

                yield return new OccupiedTileCell(
                    new Vector2Int(position.x + x, position.y + y),
                    cell.AsView());
            }
        }
    }

    public int CountNonEmptyCells()
    {
        if (_nonEmptyCellCount >= 0)
            return _nonEmptyCellCount;

        int count = 0;
        foreach (OccupiedTileCell _ in EnumerateOccupiedCells())
            count++;

        _nonEmptyCellCount = count;
        return count;
    }

    public int CountOverflowAllocations()
    {
        if (_tileCells == null)
            return 0;

        int count = 0;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (_tileCells[x, y].HasOverflowAllocation)
                    count++;
            }
        }

        return count;
    }

    private bool TryGetCellIndex(Vector2Int worldPos, out int x, out int y)
    {
        if (_tileCells == null || _tileCells.Length == 0)
        {
            if (!_hasLoggedStorageNotInitialized)
            {
                _hasLoggedStorageNotInitialized = true;
                Debug.LogError("[Data_TileMap] 地形存储未初始化，请先调用 EnsureTileStorage()。");
            }

            x = 0;
            y = 0;
            return false;
        }

        Vector2Int localPos = worldPos - position;
        x = localPos.x;
        y = localPos.y;
        return IsLocalCellValid(x, y);
    }

    private bool IsLocalCellValid(int x, int y)
    {
        return _tileCells != null && (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    private void InvalidateNonEmptyCount()
    {
        _nonEmptyCellCount = -1;
    }
}
