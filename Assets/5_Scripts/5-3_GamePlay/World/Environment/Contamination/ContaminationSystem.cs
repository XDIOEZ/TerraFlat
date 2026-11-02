using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 污染层统一访问入口：按地格读取、修改和归一化污染负荷。
/// 所有运行时写入必须经过这里，才能同时维护权威、差量存档和后续观察者通知。
/// </summary>
public static class ContaminationSystem
{
    private readonly struct DynamicCellKey : IEquatable<DynamicCellKey>
    {
        public DynamicCellKey(string worldKey, Vector2Int worldCell, string contaminationId)
        { WorldKey = worldKey; WorldCell = worldCell; ContaminationId = contaminationId; }

        public string WorldKey { get; }
        public Vector2Int WorldCell { get; }
        public string ContaminationId { get; }

        public bool Equals(DynamicCellKey other)
            => string.Equals(WorldKey, other.WorldKey, StringComparison.Ordinal) && WorldCell == other.WorldCell &&
               string.Equals(ContaminationId, other.ContaminationId, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is DynamicCellKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(WorldKey ?? string.Empty), WorldCell,
            StringComparer.Ordinal.GetHashCode(ContaminationId ?? string.Empty));
    }

    private sealed class DynamicSource
    {
        public string WorldKey;
        public string ContaminationId;
        public Vector2 Position;
        public float Amount;
        public float Radius;
        public List<KeyValuePair<DynamicCellKey, double>> Contributions;
    }

    private sealed class DynamicCellValues
    {
        public readonly Dictionary<object, double> Contributions = new();
        public double Total;
    }

    public static event Action<ContaminationCellChanged> Changed;

    private static readonly Dictionary<object, DynamicSource> dynamicSources = new();
    private static readonly Dictionary<DynamicCellKey, DynamicCellValues> dynamicValues = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDynamicSources()
    {
        dynamicSources.Clear();
        dynamicValues.Clear();
        Changed = null;
    }

    /// <summary>查询已注册的污染定义。</summary>
    public static bool TryGetDefinition(string contaminationId, out ContaminationDefinition definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(contaminationId) || GameRes.ExistingInstance == null)
            return false;
        definition = GameRes.ExistingInstance.GetContaminationDefinition(contaminationId.Trim());
        return definition != null;
    }

    /// <summary>读取一个已加载世界地格的污染值。</summary>
    public static bool TryGetValue(Vector2 worldPosition, string contaminationId, out float value)
    {
        value = 0f;
        if (!TryGetDefinition(contaminationId, out ContaminationDefinition definition) ||
            ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        value = ReadCombinedValue(sample, definition);
        return true;
    }

    #region 地块总体污染负荷

    // 一次定位真实地格，再汇总所有登记的污染指标，查询失败不当成干净地块。
    public static bool TryGetNormalizedLoad(Vector2 worldPosition, out float load)
    {
        load = 0f;
        GameRes resources = GameRes.ExistingInstance;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (resources == null || chunks == null ||
            !chunks.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
            return false;
        foreach (ContaminationDefinition definition in resources.ContaminationDefinitions.Values)
        {
            load = Mathf.Max(load, definition.Normalize(ReadCombinedValue(sample, definition)));
            if (load >= 1f) break;
        }
        return true;
    }

    public static float ReadNormalizedLoad(ChunkTerrainData terrain, Vector2Int localCell,
        Dictionary<string, ContaminationDefinition> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));
        float load = 0f;
        foreach (ContaminationDefinition definition in definitions.Values)
        {
            float value = ReadValue(terrain, localCell, definition);
            load = Mathf.Max(load, definition.Normalize(value));
            if (load >= 1f)
                break;
        }
        return load;
    }

    #endregion

    /// <summary>读取污染值；环境层尚未创建时返回定义的默认背景值。</summary>
    public static float ReadValue(ChunkTerrainData terrain, Vector2Int localCell, ContaminationDefinition definition)
    {
        if (terrain == null || terrain.IsDisposed)
            throw new ArgumentNullException(nameof(terrain));
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        return terrain.TryGetEnvironmentValue(
            definition.EnvironmentLayerId,
            localCell.x,
            localCell.y,
            out float value)
            ? definition.Clamp(value)
            : definition.DefaultValue;
    }

    private static float ReadCombinedValue(RuntimeTerrainTileSample sample, ContaminationDefinition definition)
    {
        float value = ReadValue(sample.Terrain, sample.LocalCell, definition);
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        var key = new DynamicCellKey(worldKey, sample.WorldCell, definition.Id);
        if (!dynamicValues.TryGetValue(key, out DynamicCellValues dynamicCellValues))
            return value;
        double combined = value + dynamicCellValues.Total;
        if (combined <= definition.MinimumValue) return definition.MinimumValue;
        if (combined >= definition.MaximumValue) return definition.MaximumValue;
        return (float)combined;
    }

    /// <summary>添加或更新不写入地块存档的动态污染来源。</summary>
    public static bool SetDynamicSource(object owner, string worldKey, Vector2 position,
        string contaminationId, float amount, float radius)
    {
        if (owner == null || string.IsNullOrWhiteSpace(worldKey) ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(amount) ||
            !float.IsFinite(radius) || radius <= 0f || radius > LocalTemperatureField.MaximumSourceRadius ||
            !TryGetDefinition(contaminationId, out ContaminationDefinition definition))
            return false;

        worldKey = worldKey.Trim();
        if (amount == 0f)
        {
            RemoveDynamicSource(owner);
            return true;
        }

        if (dynamicSources.TryGetValue(owner, out DynamicSource previous) &&
            string.Equals(previous.WorldKey, worldKey, StringComparison.Ordinal) &&
            string.Equals(previous.ContaminationId, definition.Id, StringComparison.Ordinal) &&
            previous.Position == position && previous.Amount == amount && previous.Radius == radius)
            return true;

        RemoveDynamicSource(owner);
        var source = new DynamicSource
        {
            WorldKey = worldKey,
            ContaminationId = definition.Id,
            Position = position,
            Amount = amount,
            Radius = radius,
            Contributions = new List<KeyValuePair<DynamicCellKey, double>>()
        };
        CaptureDynamicSourceContributions(source);
        dynamicSources.Add(owner, source);
        foreach (KeyValuePair<DynamicCellKey, double> contribution in source.Contributions)
            AddDynamicValue(contribution.Key, owner, contribution.Value);
        return true;
    }

    /// <summary>撤销指定物品来源并恢复读取到底层地块值。</summary>
    public static void RemoveDynamicSource(object owner)
    {
        if (owner == null || !dynamicSources.Remove(owner, out DynamicSource source))
            return;
        foreach (KeyValuePair<DynamicCellKey, double> contribution in source.Contributions)
            RemoveDynamicValue(contribution.Key, owner);
    }

    private static void CaptureDynamicSourceContributions(DynamicSource source)
    {
        GameSaveData save = SaveDataMgr.GetExistingInstance()?.SaveData;
        PlanetData planet = null;
        WorldTopologyBounds bounds = default;
        bool hasPlanet = !GameplayWorldRegistry.UsesLocalCoordinates(source.WorldKey) &&
            save?.PlanetData_Dict?.TryGetValue(source.WorldKey, out planet) == true;
        bool hasBounds = hasPlanet && WorldTopologyBounds.TryCreate(planet, out bounds);
        BuildDynamicSourceContributions(source, bounds, hasBounds);
    }

    private static void BuildDynamicSourceContributions(DynamicSource source,
        WorldTopologyBounds bounds, bool hasBounds)
    {
        Vector2 center = hasBounds && bounds.IsWrapped
            ? bounds.NormalizePosition(source.Position)
            : source.Position;
        float radiusSquared = source.Radius * source.Radius;
        int minX = Mathf.FloorToInt(center.x - source.Radius);
        int maxX = Mathf.FloorToInt(center.x + source.Radius);
        int minY = Mathf.FloorToInt(center.y - source.Radius);
        int maxY = Mathf.FloorToInt(center.y + source.Radius);
        var weights = new Dictionary<DynamicCellKey, double>();

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector2Int cell = new(x, y);
            if (hasBounds && bounds.IsWrapped)
                cell = bounds.NormalizeCell(cell);
            Vector2 cellCenter = new(cell.x + 0.5f, cell.y + 0.5f);
            Vector2 delta = hasBounds && bounds.IsWrapped
                ? bounds.ShortestDelta(center, cellCenter)
                : cellCenter - center;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= radiusSquared)
                continue;

            float falloff = 1f - distanceSquared / radiusSquared;
            double value = source.Amount * (double)falloff * falloff;
            var key = new DynamicCellKey(source.WorldKey, cell, source.ContaminationId);
            if (!weights.TryGetValue(key, out double previous) || Math.Abs(value) > Math.Abs(previous))
                weights[key] = value;
        }

        source.Contributions.AddRange(weights);
    }

    private static void AddDynamicValue(DynamicCellKey key, object owner, double contribution)
    {
        if (!dynamicValues.TryGetValue(key, out DynamicCellValues values))
            dynamicValues.Add(key, values = new DynamicCellValues());
        values.Contributions[owner] = contribution;
        RecalculateDynamicValue(values);
    }

    private static void RemoveDynamicValue(DynamicCellKey key, object owner)
    {
        if (!dynamicValues.TryGetValue(key, out DynamicCellValues values) || !values.Contributions.Remove(owner))
            return;
        if (values.Contributions.Count == 0)
        {
            dynamicValues.Remove(key);
            return;
        }
        RecalculateDynamicValue(values);
    }

    private static void RecalculateDynamicValue(DynamicCellValues values)
    {
        double total = 0d;
        foreach (double contribution in values.Contributions.Values)
            total += contribution;
        values.Total = total;
    }

    /// <summary>设置一个世界地格的污染值；普通客户端不能改权威世界状态。</summary>
    public static bool TrySetValue(Vector2 worldPosition, string contaminationId, float value)
    {
        if (!GameNetwork.HasStateAuthority || ChunkMgr.ExistingInstance == null ||
            !TryGetDefinition(contaminationId, out ContaminationDefinition definition) ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        return TrySetValue(sample, definition, value);
    }

    /// <summary>在当前值上增减污染负荷，并使用定义区间统一钳制。</summary>
    public static bool TryAddValue(Vector2 worldPosition, string contaminationId, float delta)
    {
        if (!GameNetwork.HasStateAuthority || ChunkMgr.ExistingInstance == null ||
            !TryGetDefinition(contaminationId, out ContaminationDefinition definition) ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        float current = ReadValue(sample.Terrain, sample.LocalCell, definition);
        return TrySetValue(sample, definition, current + delta);
    }

    /// <summary>把一个地格上的全部已注册污染指标恢复到各自默认值。</summary>
    public static bool TryClearCell(Vector2 worldPosition)
    {
        if (!GameNetwork.HasStateAuthority || GameRes.ExistingInstance == null ||
            ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        bool changed = false;
        foreach (ContaminationDefinition definition in GameRes.ExistingInstance.ContaminationDefinitions.Values)
        {
            float current = ReadValue(sample.Terrain, sample.LocalCell, definition);
            if (definition.IsDefault(current))
                continue;
            changed |= TrySetValue(sample, definition, definition.DefaultValue);
        }
        return changed;
    }

    /// <summary>恢复存档值；不重复写存档，也不发布玩家运行时修改事件。</summary>
    internal static void RestoreValue(
        ChunkTerrainData terrain,
        Vector2Int localCell,
        string contaminationId,
        float value)
    {
        if (!TryGetDefinition(contaminationId, out ContaminationDefinition definition))
            throw new InvalidOperationException($"存档引用了未注册的污染定义：{contaminationId}");
        terrain.SetEnvironmentValue(
            definition.EnvironmentLayerId,
            localCell.x,
            localCell.y,
            definition.Clamp(value));
    }

    /// <summary>对已解析地格执行一次权威写入并立即维护差量存档。</summary>
    private static bool TrySetValue(
        RuntimeTerrainTileSample sample,
        ContaminationDefinition definition,
        float value)
    {
        float previous = ReadValue(sample.Terrain, sample.LocalCell, definition);
        float next = definition.Clamp(value);
        if (Mathf.Approximately(previous, next))
            return true;

        sample.Terrain.SetEnvironmentValue(
            definition.EnvironmentLayerId,
            sample.LocalCell.x,
            sample.LocalCell.y,
            next);
        SaveDataMgr.Instance.RecordContaminationValue(sample, definition.Id, next);
        Changed?.Invoke(new ContaminationCellChanged(
            sample.Address,
            sample.WorldCell,
            sample.LocalCell,
            definition.Id,
            previous,
            next));
        return true;
    }
}

/// <summary>一次地格污染值变化；供疾病、表现或未来网络增量同步按需订阅。</summary>
public readonly struct ContaminationCellChanged
{
    public ContaminationCellChanged(
        RuntimeWorldAddress address,
        Vector2Int worldCell,
        Vector2Int localCell,
        string contaminationId,
        float previousValue,
        float currentValue)
    {
        Address = address;
        WorldCell = worldCell;
        LocalCell = localCell;
        ContaminationId = contaminationId;
        PreviousValue = previousValue;
        CurrentValue = currentValue;
    }

    public RuntimeWorldAddress Address { get; }
    public Vector2Int WorldCell { get; }
    public Vector2Int LocalCell { get; }
    public string ContaminationId { get; }
    public float PreviousValue { get; }
    public float CurrentValue { get; }
}
