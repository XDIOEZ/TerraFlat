using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MemoryPack;

public static partial class MachineWorld
{
    #region 机械存档的独立冻结与复用
    private sealed class FrozenMachineSnapshot
    {
        internal byte[] Payload;
        internal ItemData ArchiveData;
        internal string Encoded;
    }

    private sealed class MachineSaveSnapshotCache
    {
        internal readonly HashSet<ItemData> Visiting = new();
        internal ItemData Source;
        internal MachineLogic Logic;
        internal MachineState State;
        internal MachineDefinition Definition;
        internal string ScopeKey;
        internal ulong Signature, Revision, Batch;
        internal bool Reusable;
        internal FrozenMachineSnapshot Frozen;
    }

    private static readonly ConditionalWeakTable<MachineEntity, MachineSaveSnapshotCache> machineSaveSnapshots = new();
    private static readonly ConditionalWeakTable<ItemData, MachineSaveSnapshotCache> archivedMachineSaveSnapshots = new();
    private static ulong nextSaveSnapshotBatch, activeSaveSnapshotBatch;

    public static IDisposable BeginSaveSnapshotCapture()
    {
        ulong previous = activeSaveSnapshotBatch;
        if (previous == 0) activeSaveSnapshotBatch = ++nextSaveSnapshotBatch;
        return new MachineSaveCaptureScope(previous);
    }

    private sealed class MachineSaveCaptureScope : IDisposable
    {
        private readonly ulong previous;
        internal MachineSaveCaptureScope(ulong value) => previous = value;
        public void Dispose() => activeSaveSnapshotBatch = previous;
    }

    private static void PublishMachineStateChanged(MachineEntity node)
    {
        unchecked { node.SaveSnapshotRevision++; }
        NodeStateChanged?.Invoke(node);
    }

    // 只有声明变化通知完整的领域状态参与跨帧复用，其他设备仍在最终同帧捕获。
    private static bool TryCalculateMachineSaveSignature(MachineEntity node, MachineSaveSnapshotCache cache, out ulong signature)
    {
        signature = 0;
        if (ItemSnapshotSerialization.IsPublicPlayerStateCapture || node.View != null || node.Definition.Fluid != null ||
            node.Logic != null && !node.Logic.SupportsUnchangedSaveSnapshotReuse ||
            node.State != null && node.State.GetType() != typeof(MachineState)) return false;
        cache.Visiting.Clear();
        if (!ItemRuntimeStateFingerprint.TryCalculate(node.Snapshot, true, false, cache.Visiting, out ulong itemSignature)) return false;
        ulong hash = itemSignature;
        void Add(ulong value) => hash = unchecked((hash ^ value) * 1099511628211UL);
        void AddFloat(float value) => Add(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        Add(unchecked((uint)node.RotationQuarterTurns)); Add(unchecked((uint)node.ConveyorMode));
        Add(node.Engaged ? 1UL : 0UL); Add(unchecked((uint)node.RatioIndex)); AddFloat(node.ElectricalStoredJoules);
        if (node.State is MachineState state)
        {
            Add(unchecked((uint)state.RotationQuarterTurns)); Add(unchecked((uint)state.ConveyorMode));
            Add(state.Engaged ? 1UL : 0UL); Add(unchecked((uint)state.RatioIndex));
            AddFloat(state.ManualSeconds); AddFloat(state.Hp); AddFloat(state.ElectricalStoredJoules);
            Add(state.Generated ? 1UL : 0UL);
            if (state.Processing is RecipeProcessingState processing)
            {
                if (processing.GetType() != typeof(RecipeProcessingState)) return false;
                AddFloat(processing.Progress);
                Add(processing.PausedForWorkingTool ? 1UL : 0UL);
                Add(unchecked((uint)(processing.RecipeId?.Length ?? -1)));
                if (processing.RecipeId != null) foreach (char value in processing.RecipeId) Add(value);
                if (!TryCalculateMachineInventorySaveSignature(processing.Input, cache.Visiting, out ulong input) ||
                    !TryCalculateMachineInventorySaveSignature(processing.Output, cache.Visiting, out ulong output)) return false;
                Add(input); Add(output);
            }
        }
        if (node.Logic != null)
        {
            Add(unchecked((uint)node.Logic.Inventories.Count));
            foreach (Inventory inventory in node.Logic.Inventories)
            {
                if (!TryCalculateMachineInventorySaveSignature(inventory.Data, cache.Visiting, out ulong value)) return false;
                Add(value);
            }
        }
        signature = hash;
        return true;
    }

    // 机器专用库存会完整编码布局，指纹还需覆盖输入绑定、槽容量和接收标签。
    private static bool TryCalculateMachineInventorySaveSignature(Inventory_Data inventory, HashSet<ItemData> visiting, out ulong signature)
    {
        if (!ItemRuntimeStateFingerprint.TryCalculateInventory(inventory, visiting, out signature)) return false;
        if (inventory == null) return true;
        ulong hash = signature;
        void Add(ulong value) => hash = unchecked((hash ^ value) * 1099511628211UL);
        void AddString(string value)
        {
            Add(unchecked((uint)(value?.Length ?? -1)));
            if (value != null) foreach (char character in value) Add(character);
        }
        AddString(inventory.ToggleActionName); AddString(inventory.UIPrefabName);
        Add(inventory.itemSlots == null ? 0UL : 1UL);
        if (inventory.itemSlots != null)
            foreach (ItemSlot slot in inventory.itemSlots)
            {
                Add(slot == null ? 0UL : 1UL);
                if (slot == null) continue;
                Add(unchecked((uint)slot.Index));
                Add(unchecked((uint)BitConverter.SingleToInt32Bits(slot.SlotMaxVolume)));
                Add(unchecked((uint)(slot.CanAcceptTags?.Count ?? -1)));
                if (slot.CanAcceptTags != null) foreach (string tag in slot.CanAcceptTags) AddString(tag);
            }
        signature = hash;
        return true;
    }

    private static FrozenMachineSnapshot FreezeMachineSaveSnapshot(MachineEntity node)
    {
        MachineSaveSnapshotCache cache = machineSaveSnapshots.GetValue(node, static _ => new MachineSaveSnapshotCache());
        bool publicCapture = ItemSnapshotSerialization.IsPublicPlayerStateCapture;
        if (!publicCapture && activeSaveSnapshotBatch != 0 && cache.Batch == activeSaveSnapshotBatch && cache.Frozen != null &&
            cache.Revision == node.SaveSnapshotRevision &&
            ReferenceEquals(cache.Source, node.Snapshot) && cache.ScopeKey == node.ScopeKey)
            return cache.Frozen;
        if (cache.Reusable && cache.Frozen != null &&
            cache.Revision == node.SaveSnapshotRevision && ReferenceEquals(cache.Source, node.Snapshot) &&
            ReferenceEquals(cache.State, node.State) && ReferenceEquals(cache.Logic, node.Logic) &&
            ReferenceEquals(cache.Definition, node.Definition) && cache.ScopeKey == node.ScopeKey &&
            TryCalculateMachineSaveSignature(node, cache, out ulong signature) && cache.Signature == signature)
        {
            cache.Batch = activeSaveSnapshotBatch;
            return cache.Frozen;
        }

        ItemData archiveData;
        {
            using IDisposable privateCapture = publicCapture ? ItemSnapshotSerialization.BeginPrivatePlayerState() : null;
            CaptureNode(node);
            archiveData = ItemInstanceDataFactory.CloneRuntime(node.Snapshot);
        }
        ItemInstanceSnapshot snapshot = ItemSnapshotSerialization.Capture(node.Snapshot);
        var frozen = new FrozenMachineSnapshot
        {
            Payload = ItemSnapshotSerialization.SerializePayload(snapshot),
            ArchiveData = archiveData
        };
        cache.Source = node.Snapshot; cache.State = node.State; cache.Logic = node.Logic;
        cache.Definition = node.Definition; cache.ScopeKey = node.ScopeKey;
        cache.Reusable = TryCalculateMachineSaveSignature(node, cache, out cache.Signature);
        cache.Revision = node.SaveSnapshotRevision; cache.Batch = activeSaveSnapshotBatch; cache.Frozen = frozen;
        return frozen;
    }

    public static string CaptureEncodedSaveSnapshot(MachineEntity node)
    {
        using var nodeScope = UseNodeScope(node);
        FrozenMachineSnapshot frozen = FreezeMachineSaveSnapshot(node);
        return frozen.Encoded ??= Convert.ToBase64String(frozen.Payload);
    }

    public static IEnumerator PrepareSaveSnapshotCache(GameSaveData save, AutoSaveWorkBudget budget)
    {
        var candidates = new List<MachineEntity>();
        foreach (ScopeState value in scopes.Values)
            if (ReferenceEquals(value.Owner, save)) candidates.AddRange(value.Nodes.Values);
        foreach (MachineEntity node in candidates)
        {
            if (scopes.TryGetValue(node.ScopeKey, out ScopeState value) && ReferenceEquals(value.Owner, save) &&
                value.Nodes.TryGetValue(node.Id, out MachineEntity current) && ReferenceEquals(node, current))
            {
                using var nodeScope = UseNodeScope(node);
                MachineSaveSnapshotCache cache = machineSaveSnapshots.GetValue(node, static _ => new MachineSaveSnapshotCache());
                if (TryCalculateMachineSaveSignature(node, cache, out _)) FreezeMachineSaveSnapshot(node);
            }
            if (budget.ShouldYield()) yield return budget.WaitForNextSlice();
        }
    }

    private static byte[] FreezeArchivedMachineSnapshot(ItemData data)
    {
        MachineSaveSnapshotCache cache = archivedMachineSaveSnapshots.GetValue(data, static _ => new MachineSaveSnapshotCache());
        cache.Visiting.Clear();
        bool reusable = ItemRuntimeStateFingerprint.TryCalculate(data, true, false, cache.Visiting, out ulong signature) &&
                        !ItemSnapshotSerialization.IsPublicPlayerStateCapture;
        if (reusable && cache.Reusable && cache.Frozen != null && cache.Signature == signature) return cache.Frozen.Payload;
        byte[] bytes = ItemSnapshotSerialization.SerializePayload(ItemSnapshotSerialization.Capture(data));
        cache.Reusable = reusable; cache.Signature = signature;
        cache.Frozen = new FrozenMachineSnapshot { Payload = bytes };
        return bytes;
    }

    private static byte[] CaptureFrozenMachineArchive(GameSaveData save)
    {
        using var batch = BeginSaveSnapshotCapture();
        MachineArchive archive = save.Mechanical ??= new MachineArchive();
        var activePayloads = new Dictionary<ItemData, byte[]>();
        ScopeState previousScope = scope;
        string previousOverride = scopeOverride;
        try
        {
            foreach (ScopeState value in scopes.Values)
            {
                if (!ReferenceEquals(value.Owner, save) || value.Graph == null) continue;
                scope = value; scopeOverride = value.Key;
                var saved = new List<ItemData>(value.Nodes.Count);
                if (archive.Worlds.TryGetValue(value.Key, out List<ItemData> previous))
                    foreach (ItemData data in previous)
                        if (MachineCatalog.Get(data.IDName) == null) saved.Add(data);
                foreach (MachineEntity node in value.Nodes.Values)
                {
                    FrozenMachineSnapshot frozen = FreezeMachineSaveSnapshot(node);
                    saved.Add(frozen.ArchiveData); activePayloads[frozen.ArchiveData] = frozen.Payload;
                }
                saved.Sort(CompareSnapshots);
                archive.Worlds[value.Key] = saved;
            }
        }
        finally { scope = previousScope; scopeOverride = previousOverride; }

        var payload = new FrozenMachineArchivePayload();
        foreach (KeyValuePair<string, List<ItemData>> world in archive.Worlds)
        {
            var items = new List<byte[]>(world.Value.Count);
            foreach (ItemData data in world.Value)
                items.Add(activePayloads.TryGetValue(data, out byte[] bytes) ? bytes : FreezeArchivedMachineSnapshot(data));
            payload.Worlds.Add(world.Key, items);
        }
        payload.Metadata = MemoryPackSerializer.Serialize(MachineArchiveMetadataSnapshot.Capture(archive));
        return MemoryPackSerializer.Serialize(payload);
    }

    private static MachineArchive RestoreFrozenMachineArchive(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return new MachineArchive();
        FrozenMachineArchivePayload frozen = MemoryPackSerializer.Deserialize<FrozenMachineArchivePayload>(bytes);
        if (frozen?.Version != 2 || frozen.Metadata == null || frozen.Worlds == null)
            throw new InvalidOperationException("不支持的机械存档版本。");
        MachineArchive archive = MemoryPackSerializer.Deserialize<MachineArchiveMetadataSnapshot>(frozen.Metadata)?.Restore();
        if (archive?.Version != 2 || archive.Worlds == null) throw new InvalidOperationException("机械存档元数据无效。");
        foreach (KeyValuePair<string, List<byte[]>> world in frozen.Worlds)
        {
            var items = new List<ItemData>(world.Value.Count);
            foreach (byte[] item in world.Value)
                items.Add(MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(item).CreateColdData());
            archive.Worlds.Add(world.Key, items);
        }
        return archive;
    }
    #endregion
}

public sealed partial class MachineEntity
{
    #region 存档变化版本
    internal ulong SaveSnapshotRevision;
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class FrozenMachineArchivePayload
{
    #region 独立机器字节与档案元数据
    [MemoryPackOrder(0)] public int Version = 2;
    [MemoryPackOrder(1)] public byte[] Metadata;
    [MemoryPackOrder(2)] public Dictionary<string, List<byte[]>> Worlds = new(StringComparer.Ordinal);
    #endregion
}
