using System;
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
    public static event Action<ContaminationCellChanged> Changed;

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

        value = ReadValue(sample.Terrain, sample.LocalCell, definition);
        return true;
    }

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
