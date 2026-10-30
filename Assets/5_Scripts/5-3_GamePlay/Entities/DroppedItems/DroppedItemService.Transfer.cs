using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

public static partial class DroppedItemService
{
    #region 掉落世界批量移交
    private static readonly Dictionary<(uint Epoch, int Id), uint> movedHandles = new();
    internal static event Action<string> WorldItemsChanged;
    internal static void NotifyWorldItemsChanged(string worldKey) => WorldItemsChanged?.Invoke(worldKey);

    internal static bool HasRetainedWorldItems(string worldKey, GameSaveData save)
    {
        if (worlds.TryGetValue(worldKey, out DropWorld world) && world.Runtime?.IsCreated == true)
            return world.Runtime.HasRetainedItems;
        return save?.DroppedItems?.Worlds != null &&
            save.DroppedItems.Worlds.TryGetValue(worldKey, out var saved) && saved.Count > 0;
    }

    internal static List<DroppedItemSaveRecord> CaptureWorldItems(string worldKey)
    {
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        EnsureContext();
        return runtime?.Capture() ?? new List<DroppedItemSaveRecord>();
    }

    internal static void ApplyNetworkWorldItems(string worldKey, IReadOnlyList<DroppedItemSaveRecord> records)
    {
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        EnsureContext();
        runtime.ApplyNetworkWorldItems(records);
    }

    public sealed class WorldExtraction : IDisposable
    {
        private readonly string worldKey;
        private readonly DropWorld world;
        private readonly List<DroppedItemSaveRecord> records;
        private bool finished;
        public IReadOnlyList<DroppedItemSaveRecord> Records => records;

        internal WorldExtraction(string worldKey)
        { this.worldKey = worldKey; world = State; records = world.Runtime.Capture(); }

        public void Commit()
        {
            if (finished) throw new InvalidOperationException("掉落物提取事务已结束。");
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            if (world.Runtime?.IsCreated != true ||
                !MemoryPackSerializer.Serialize(records).AsSpan().SequenceEqual(MemoryPackSerializer.Serialize(world.Runtime.Capture())))
                throw new InvalidOperationException("掉落物提取期间来源已变化。");
            var removed = new List<DroppedItemSaveRecord>();
            try
            {
                foreach (DroppedItemSaveRecord record in records)
                {
                    if (record == null || !record.HasItemPayload || record.Guid == 0) throw new InvalidOperationException("掉落物提取载荷无效。");
                    world.Runtime.RemoveCapturedWorldItem(record.Guid);
                    removed.Add(record);
                }
                finished = true;
            }
            catch
            {
                foreach (DroppedItemSaveRecord record in removed) world.Runtime.RestoreCapturedWorldItem(record);
                throw;
            }
        }

        public void Dispose() => finished = true;
    }

    public static WorldExtraction ExtractWorldItems(string worldKey)
    {
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        EnsureContext();
        return new WorldExtraction(worldKey);
    }

    private static uint ResolveHandleEpoch(DroppedItemHandle handle)
        => movedHandles.TryGetValue((handle.Epoch, handle.Id), out uint epoch) ? epoch : handle.Epoch;

    private static void MoveHandleAliases(uint sourceEpoch, uint targetEpoch, IReadOnlyList<int> ids)
    {
        var keys = new List<(uint Epoch, int Id)>();
        foreach (int id in ids)
        {
            keys.Clear();
            foreach (var pair in movedHandles)
                if (pair.Key.Id == id && pair.Value == sourceEpoch) keys.Add(pair.Key);
            foreach (var key in keys)
                if (key.Epoch == targetEpoch) movedHandles.Remove(key);
                else movedHandles[key] = targetEpoch;
            if (sourceEpoch != targetEpoch) movedHandles[(sourceEpoch, id)] = targetEpoch;
        }
    }

    public sealed class WorldTransfer : IDisposable
    {
        private readonly string source, destination;
        private readonly HashSet<int> ids;
        private readonly Func<Vector2, Vector2> reverse;
        private readonly float reverseRotation;
        private bool finished;

        internal WorldTransfer(string source, string destination, List<int> ids,
            Func<Vector2, Vector2> reverse, float reverseRotation)
        { this.source = source; this.destination = destination; this.ids = new HashSet<int>(ids); this.reverse = reverse; this.reverseRotation = reverseRotation; }

        public void Commit() => finished = true;
        public void Dispose()
        {
            if (finished) return;
            TransferWorldItemsCore(destination, source, position => true, reverse, reverseRotation, ids);
            finished = true;
        }
    }

    public static WorldTransfer TransferWorldItems(string source, string destination,
        Predicate<Vector2> select, Func<Vector2, Vector2> project, Func<Vector2, Vector2> reverse,
        float rotationDegrees = 0f)
    {
        if (select == null || project == null || reverse == null) throw new ArgumentNullException();
        List<int> ids = TransferWorldItemsCore(source, destination, select, project, rotationDegrees, null);
        return new WorldTransfer(source, destination, ids, reverse, -rotationDegrees);
    }

    private static List<int> TransferWorldItemsCore(string source, string destination,
        Predicate<Vector2> select, Func<Vector2, Vector2> project, float rotationDegrees, HashSet<int> identities)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination) || source == destination)
            throw new ArgumentException("掉落物迁移需要不同且已登记的世界。");
        DropWorld from, to;
        using (GameplayWorldRegistry.UseWorld(source)) { EnsureContext(); from = State; }
        using (GameplayWorldRegistry.UseWorld(destination)) { EnsureContext(); to = State; }
        Predicate<Vector2> filter = identities == null ? select : _ => true;
        List<int> moved;
        moved = from.Runtime.TransferTo(to.Runtime, source, destination, filter, project, rotationDegrees, identities);
        MoveHandleAliases(from.Epoch, to.Epoch, moved);
        return moved;
    }
    #endregion
}
