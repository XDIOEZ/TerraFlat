using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 玩家主世界初始出生点存档：只在第一次确定安全陆地后写入，之后死亡复活始终读取同一坐标。
/// 使用 ItemSpecialData JSON 命名空间，避免改变 Data_Player 的 MemoryPack 布局并兼容旧存档。
/// </summary>
public static class PlayerMainWorldSpawnStore
{
    #region 常量

    private const string NamespaceKey = "flatworld.playerSpawn";
    private const string WorldKeyProperty = "mainWorldKey";
    private const string PositionProperty = "position";

    #endregion

    #region 读取与写入

    /// <summary>读取玩家存档中的主世界出生点。</summary>
    public static bool TryGetMainWorldSpawn(
        Data_Player playerData,
        out Vector3 position,
        out string worldKey)
    {
        position = default;
        worldKey = string.Empty;
        if (playerData == null)
            return false;

        JObject spawnData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        string storedWorldKey = spawnData.Value<string>(WorldKeyProperty);
        if (string.IsNullOrWhiteSpace(storedWorldKey) ||
            !TryReadPosition(spawnData[PositionProperty] as JObject, out position))
        {
            position = default;
            return false;
        }

        worldKey = NormalizeSurfaceWorldKey(storedWorldKey);
        return !string.IsNullOrWhiteSpace(worldKey);
    }

    /// <summary>写入主世界出生点；只接受有限坐标，世界键统一保存为地表键。</summary>
    public static bool SetMainWorldSpawn(
        Data_Player playerData,
        string worldKey,
        Vector3 position)
    {
        if (playerData == null ||
            !TryNormalizePosition(position, out Vector3 normalizedPosition))
        {
            return false;
        }

        string normalizedWorldKey = NormalizeSurfaceWorldKey(worldKey);
        if (string.IsNullOrWhiteSpace(normalizedWorldKey))
            return false;

        JObject spawnData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        spawnData[WorldKeyProperty] = normalizedWorldKey;
        spawnData[PositionProperty] = new JObject
        {
            ["x"] = normalizedPosition.x,
            ["y"] = normalizedPosition.y,
            ["z"] = normalizedPosition.z
        };
        ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, spawnData);
        return true;
    }

    /// <summary>仅在旧存档没有出生点时写入，避免每次加载覆盖原始出生位置。</summary>
    public static bool EnsureMainWorldSpawn(
        Data_Player playerData,
        string worldKey,
        Vector3 position)
    {
        return TryGetMainWorldSpawn(playerData, out _, out _) ||
               SetMainWorldSpawn(playerData, worldKey, position);
    }

    #endregion

    #region 校验

    private static string NormalizeSurfaceWorldKey(string worldKey)
    {
        if (string.IsNullOrWhiteSpace(worldKey))
            return string.Empty;

        return WorldAddress.FromWorldKey(worldKey).PlanetId;
    }

    private static bool TryReadPosition(JObject positionData, out Vector3 position)
    {
        position = default;
        if (positionData == null)
            return false;

        float? x = positionData.Value<float?>("x");
        float? y = positionData.Value<float?>("y");
        float? z = positionData.Value<float?>("z");
        if (!x.HasValue || !y.HasValue || !z.HasValue ||
            !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(z.Value))
        {
            return false;
        }

        position = new Vector3(x.Value, y.Value, z.Value);
        return true;
    }

    private static bool TryNormalizePosition(Vector3 position, out Vector3 normalizedPosition)
    {
        normalizedPosition = new Vector3(position.x, position.y, 0f);
        return IsFinite(normalizedPosition.x) &&
               IsFinite(normalizedPosition.y) &&
               IsFinite(normalizedPosition.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    #endregion
}
