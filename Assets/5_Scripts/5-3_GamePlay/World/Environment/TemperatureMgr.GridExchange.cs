using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class TemperatureMgr
{
    #region 按来源替换动态网格交换结果
    public bool ReplaceDynamicGridTemperatureExchangeTargets(object owner, IReadOnlyDictionary<Vector2Int, float> targets)
    {
        PrepareFieldContext();
        TemperatureWorld state = FieldState;
        if (gridExchangeOwners.TryGetValue(owner, out TemperatureWorld previous) && !ReferenceEquals(previous, state))
        {
            if (previous.gridTemperatureExchange?.RemoveSource(owner) == true) previous.fieldContextVersion++;
        }
        if (state.gridTemperatureExchange == null)
        {
            gridExchangeOwners.Remove(owner);
            return false;
        }
        var offsets = state.gridExchangeOffsets;
        offsets.Clear();
        using var sampling = liquidTemperatureField?.BeginPointSamplingBatch() ?? default;
        foreach (var pair in targets)
        {
            Vector2 position = (Vector2)pair.Key + Vector2.one * .5f;
            if (fieldBounds.IsWrapped) position = fieldBounds.NormalizePosition(position);
            Vector2Int cell = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
            Int2 origin = new(Mathf.FloorToInt((float)cell.x / fieldChunkWidth) * fieldChunkWidth,
                Mathf.FloorToInt((float)cell.y / fieldChunkHeight) * fieldChunkHeight);
            var address = new FlatWorld.WorldModel.WorldAddress(fieldDimension, origin);
            if (!fieldWorld.Chunks.TryGetValue(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null || chunk.Terrain.IsDisposed ||
                !TrySamplePreparedTemperature(position, true, out float ambient, out _, true, sampling, includeGridExchange: false)) continue;
            // 新偏移相对不含交换层的实时环境计算，旧偏移不重复累加。
            offsets[cell] = new DynamicGridTemperatureExchangeField.CellOffset(chunk.Terrain, pair.Value - ambient);
        }
        bool changed = state.gridTemperatureExchange.ReplaceSourceOffsets(owner, offsets);
        if (offsets.Count == 0) gridExchangeOwners.Remove(owner);
        else gridExchangeOwners[owner] = state;
        offsets.Clear();
        if (changed) state.fieldContextVersion++;
        return changed;
    }

    public void RemoveDynamicGridTemperatureExchangeTargets(object owner)
    {
        if (!gridExchangeOwners.Remove(owner, out TemperatureWorld state)) return;
        if (state.gridTemperatureExchange?.RemoveSource(owner) == true) state.fieldContextVersion++;
    }
    #endregion
}
