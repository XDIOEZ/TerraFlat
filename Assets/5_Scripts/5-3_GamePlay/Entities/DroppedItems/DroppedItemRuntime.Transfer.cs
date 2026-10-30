using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using MemoryPack;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 轻量掉落跨世界事务
    internal bool HasRetainedItems => simulation.Count > 0 || unresolved.Count > 0;

    internal void RemoveCapturedWorldItem(int id)
    {
        Remove(id);
        unresolved.RemoveAll(record => record?.Guid == id);
    }

    internal void RestoreCapturedWorldItem(DroppedItemSaveRecord record) => Restore(record);

    internal void ApplyNetworkWorldItems(IReadOnlyList<DroppedItemSaveRecord> records)
    {
        var incoming = new Dictionary<int, DroppedItemSaveRecord>();
        foreach (DroppedItemSaveRecord record in records)
        {
            int id = record?.Guid ?? 0;
            if (id == 0 || !incoming.TryAdd(id, record))
                throw new InvalidOperationException("网络掉落物身份无效或重复。");
        }
        foreach (DroppedItemSaveRecord previous in Capture())
        {
            int id = previous.Guid;
            if (incoming.TryGetValue(id, out var next) &&
                MemoryPackSerializer.Serialize(previous).AsSpan().SequenceEqual(MemoryPackSerializer.Serialize(next)))
            {
                incoming.Remove(id);
                continue;
            }
            Remove(id);
            unresolved.RemoveAll(record => record?.Guid == id);
        }
        foreach (DroppedItemSaveRecord record in incoming.Values) Restore(record);
    }

    internal List<int> TransferTo(DroppedItemRuntime target, string sourceWorld, string targetWorld,
        Predicate<Vector2> select, Func<Vector2, Vector2> project, float rotationDegrees, HashSet<int> onlyIds)
    {
        var records = Capture();
        var selected = new List<DroppedItemSaveRecord>();
        var projected = new List<DroppedItemSaveRecord>();
        var identities = new HashSet<int>();
        using (GameplayWorldRegistry.UseWorld(sourceWorld))
        foreach (DroppedItemSaveRecord record in records)
        {
            if (record == null || !select(record.Position)) continue;
            int id = record.Guid;
            if (onlyIds != null && !onlyIds.Contains(id)) continue;
            if (id <= 0 || !identities.Add(id) || target.simulation.Contains(id) ||
                target.unresolved.Exists(value => value?.Guid == id))
                throw new InvalidOperationException("掉落物迁移包含无效或重复身份。");
            selected.Add(record);
            DroppedItemSaveRecord copy = record.CopyForWorldTransfer();
            copy.Position = project(copy.Position);
            copy.Rotation += rotationDegrees;
            if (copy.HasFlight)
            {
                copy.FlightStart = project(copy.FlightStart);
                copy.FlightEnd = project(copy.FlightEnd);
                copy.FlightControl = project(copy.FlightControl);
            }
            projected.Add(copy);
        }

        var imported = new List<int>(selected.Count);
        var unresolvedCopies = new List<DroppedItemSaveRecord>();
        try
        {
            using var scope = GameplayWorldRegistry.UseWorld(targetWorld);
            for (int i = 0; i < selected.Count; i++)
            {
                DroppedItemSaveRecord original = selected[i];
                DroppedItemSaveRecord copy = projected[i];
                if (!simulation.Contains(original.Guid))
                {
                    target.unresolved.Add(copy);
                    unresolvedCopies.Add(copy);
                    continue;
                }
                target.Restore(copy);
                if (!target.simulation.Contains(copy.Guid))
                {
                    target.unresolved.Remove(copy);
                    throw new InvalidOperationException("目标世界不能恢复掉落物：" + copy.Data.IDName);
                }
                imported.Add(copy.Guid);
            }
        }
        catch
        {
            using var scope = GameplayWorldRegistry.UseWorld(targetWorld);
            foreach (int id in imported) target.Remove(id);
            foreach (DroppedItemSaveRecord record in unresolvedCopies) target.unresolved.Remove(record);
            throw;
        }

        using (GameplayWorldRegistry.UseWorld(sourceWorld))
            foreach (DroppedItemSaveRecord record in selected)
                if (simulation.Contains(record.Guid)) Remove(record.Guid);
                else unresolved.Remove(record);
        return new List<int>(identities);
    }
    #endregion
}
