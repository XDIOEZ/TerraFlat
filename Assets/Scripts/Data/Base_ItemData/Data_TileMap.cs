using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [HideInInspector]
    [SerializeField]
    [Obsolete("?????????????")]
    public Dictionary<Vector2Int, List<TileData>> TileData = new();

    [Tooltip("µØÍ¼µÄÎ»ÖÃ")]
    public Vector2Int position = new Vector2Int(0, 0);

    public bool TileLoaded = false;

    public EnvironmentFactors[,] EnvironmentData = new EnvironmentFactors[0, 0];
    public List<TileData>[,] TileData_Array = new List<TileData>[20, 20];//00??????? ????position??

    #region TileData ?????
    public int Width => TileData_Array != null && TileData_Array.Length > 0 ? TileData_Array.GetLength(0) : 0;
    public int Height => TileData_Array != null && TileData_Array.Length > 0 ? TileData_Array.GetLength(1) : 0;

    public void EnsureTileDataArray(int width, int height, bool initCells = true)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[Data_TileMap] ? EnsureTileDataArray ?????{width}x{height}");
            return;
        }

        if (TileData_Array == null || TileData_Array.GetLength(0) != width || TileData_Array.GetLength(1) != height)
        {
            TileData_Array = new List<TileData>[width, height];
        }

        if (!initCells)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileData_Array[x, y] ??= new List<TileData>(capacity: 2);
            }
        }
    }

    public void ClearAllTiles(bool clearLegacyDictionary = false)
    {
        if (TileData_Array != null && TileData_Array.Length > 0)
        {
            int w = TileData_Array.GetLength(0);
            int h = TileData_Array.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    TileData_Array[x, y]?.Clear();
                }
            }
        }

        if (clearLegacyDictionary)
        {
#pragma warning disable 0618
            TileData?.Clear();
#pragma warning restore 0618
        }
    }

    /// <summary>
    /// ? TileData_Array ????????????????????????
    /// </summary>
    public void BuildArrayFromLegacyDictionaryIfNeeded(int width, int height)
    {
        if (TileData_Array != null && TileData_Array.Length > 0)
            return;

        EnsureTileDataArray(width, height, initCells: true);

#pragma warning disable 0618
        if (TileData == null || TileData.Count == 0)
            return;

        foreach (var kvp in TileData)
        {
            Vector2Int worldPos = kvp.Key;
            Vector2Int localPos = worldPos - position;
            if ((uint)localPos.x >= (uint)width || (uint)localPos.y >= (uint)height)
                continue;

            var src = kvp.Value;
            if (src == null || src.Count == 0)
                continue;

            var dst = TileData_Array[localPos.x, localPos.y] ??= new List<TileData>(capacity: src.Count);
            dst.Clear();
            dst.AddRange(src);
        }
#pragma warning restore 0618
    }

    public void SyncLegacyDictionaryFromArray()
    {
#pragma warning disable 0618
        TileData ??= new Dictionary<Vector2Int, List<TileData>>();
        TileData.Clear();
#pragma warning restore 0618

        if (TileData_Array == null || TileData_Array.Length == 0)
            return;

        int w = TileData_Array.GetLength(0);
        int h = TileData_Array.GetLength(1);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var list = TileData_Array[x, y];
                if (list == null || list.Count == 0)
                    continue;

                Vector2Int worldPos = new Vector2Int(position.x + x, position.y + y);
                var copy = new List<TileData>(list);
#pragma warning disable 0618
                TileData[worldPos] = copy;
#pragma warning restore 0618
            }
        }
    }

    #endregion

    #region TileData ??
    /// <summary>
    /// ????????? TileData?
    /// ???? TileData_Array????????????/??/??????? TileData ???????????
    /// ??????????????
    /// </summary>
    public TileData GetTileDataAt(Vector2Int worldPos, int? index = null)
    {
        var list = GetTileListAt(worldPos);
        if (list == null || list.Count == 0)
            return null;

        int i = index ?? (list.Count - 1);
        if (i < 0 || i >= list.Count)
        {
            Debug.LogWarning($"?? {worldPos} ??? {i} ????", null);
            return null;
        }

        return list[i];
    }

    public List<TileData> GetTileListAt(Vector2Int worldPos)
    {
        // ?????????O(1)
        if (TileData_Array != null && TileData_Array.Length > 0)
        {
            Vector2Int localPos = worldPos - position;
            int w = TileData_Array.GetLength(0);
            int h = TileData_Array.GetLength(1);

            if ((uint)localPos.x < (uint)w && (uint)localPos.y < (uint)h)
            {
                return TileData_Array[localPos.x, localPos.y];
            }
        }

        // ???????
#pragma warning disable 0618
        if (TileData != null && TileData.TryGetValue(worldPos, out var dictList))
        {
            return dictList;
        }
#pragma warning restore 0618

        return null;
    }
    #endregion

    #region TileData ??
    public void AddTileData(Vector2Int worldPos, TileData tileData)
    {
        Vector2Int localPos = worldPos - position;
        int w = Width;
        int h = Height;
        if ((uint)localPos.x >= (uint)w || (uint)localPos.y >= (uint)h)
        {
            Debug.LogError($"[Data_TileMap] ? AddTileData ???world={worldPos} local={localPos} size={w}x{h}");
            return;
        }

        var list = TileData_Array[localPos.x, localPos.y] ??= new List<TileData>(capacity: 2);
        list.Add(tileData);
    }

    public bool RemoveTileData(Vector2Int worldPos, int? index = null)
    {
        var list = GetTileListAt(worldPos);
        if (list == null || list.Count == 0)
            return false;

        int i = index ?? (list.Count - 1);
        if ((uint)i >= (uint)list.Count)
        {
            Debug.LogWarning($"?? {worldPos} ????? {i} ???", null);
            return false;
        }

        list.RemoveAt(i);
        return true;
    }

    public bool UpdateTileData(Vector2Int worldPos, int index, TileData tileData)
    {
        var list = GetTileListAt(worldPos);
        if (list == null || list.Count == 0)
            return false;

        if ((uint)index >= (uint)list.Count)
        {
            Debug.LogWarning($"?? {worldPos} ??? {index} ????", null);
            return false;
        }

        list[index] = tileData;
        return true;
    }
    #endregion

    #region ??
    public IEnumerable<(Vector2Int worldPos, List<TileData> list)> EnumerateNonEmptyTiles()
    {
        if (TileData_Array == null || TileData_Array.Length == 0)
            yield break;

        int w = TileData_Array.GetLength(0);
        int h = TileData_Array.GetLength(1);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var list = TileData_Array[x, y];
                if (list == null || list.Count == 0)
                    continue;

                yield return (new Vector2Int(position.x + x, position.y + y), list);
            }
        }
    }

    public int CountNonEmptyCells()
    {
        int count = 0;
        foreach (var _ in EnumerateNonEmptyTiles())
            count++;
        return count;
    }
    #endregion
}