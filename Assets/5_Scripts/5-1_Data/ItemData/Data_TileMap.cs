using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [NonSerialized] private bool _hasLoggedArrayNotInit;
    [Tooltip("地图的位置")]
    public Vector2Int position = new Vector2Int(0, 0);
    public bool TileLoaded = false;
    [Tooltip("环境多层网格（性能优先主存储）")]
    public EnvironmentLayers EnvironmentLayers = new EnvironmentLayers();
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

    public void ClearAllTiles()
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
    }

    #endregion

    #region Environment Layers

    public void EnsureEnvironmentStorage(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[Data_TileMap] EnsureEnvironmentStorage 参数非法：{width}x{height}");
            return;
        }

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
        float humidity,
        float precipitation,
        float solidity,
        float hight,
        float pollution = 0f)
    {
        if (!IsEnvironmentLocalValid(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"[Data_TileMap] 环境坐标越界：({x},{y}) size={EnvironmentLayers?.Width}x{EnvironmentLayers?.Height}");
        }

        EnvironmentLayers.SetCell(x, y, temperature, temperatureCelsius, humidity, precipitation, solidity, hight, pollution);
    }

    public void SetHumidityAtLocal(int x, int y, float humidity)
    {
        if (!IsEnvironmentLocalValid(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"[Data_TileMap] 环境坐标越界：({x},{y}) size={EnvironmentLayers?.Width}x{EnvironmentLayers?.Height}");
        }

        EnvironmentLayers.SetHumidity(x, y, humidity);
    }

    public void SetSolidityAtLocal(int x, int y, float solidity)
    {
        if (!IsEnvironmentLocalValid(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"[Data_TileMap] 环境坐标越界：({x},{y}) size={EnvironmentLayers?.Width}x{EnvironmentLayers?.Height}");
        }

        EnvironmentLayers.SetSolidity(x, y, solidity);
    }

    public void SetLightAtLocal(int x, int y, float light)
    {
        if (!IsEnvironmentLocalValid(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"[Data_TileMap] 光照坐标越界：({x},{y}) size={EnvironmentLayers?.Width}x{EnvironmentLayers?.Height}");
        }

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
        if (TileData_Array == null || TileData_Array.Length == 0)
        {
            if (!_hasLoggedArrayNotInit)
            {
                _hasLoggedArrayNotInit = true;
                Debug.LogError("[Data_TileMap] ? TileData_Array ???????????? EnsureTileDataArray()", null);
            }
            return null;
        }

        Vector2Int localPos = worldPos - position;
        int w = TileData_Array.GetLength(0);
        int h = TileData_Array.GetLength(1);
        if ((uint)localPos.x >= (uint)w || (uint)localPos.y >= (uint)h)
            return null;

        return TileData_Array[localPos.x, localPos.y];
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
