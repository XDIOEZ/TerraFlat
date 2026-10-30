using System;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>区块持有引用，实体状态只保存于规范锚点所属区块，与表现宿主无关。</summary>
internal sealed class SharedNaturalEntityRegistry : IDisposable
{
    #region 共享身份与区块引用

    internal sealed class Entry
    {
        internal RuntimeWorldAddress Address;
        internal Int2 Anchor;
        internal int Guid;
        internal string ItemId;
        internal NaturalEntityHandle Handle;
        internal bool Removed;
        internal readonly SharedEntityLeaseSet<Lease> Owners = new();
    }

    internal sealed class Lease
    {
        internal SharedNaturalEntityRegistry Registry;
        internal Entry Entry;
        internal ChunkTilemapRenderer Owner;
        internal bool Released;
    }

    private readonly ChunkMgr manager;
    private readonly string worldKey;
    private readonly Dictionary<(RuntimeWorldAddress, int), Entry> entries = new();
    private bool disposed;

    internal SharedNaturalEntityRegistry(ChunkMgr manager)
    {
        this.manager = manager;
        worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        manager.NaturalItemRemoved += OnRemoved;
    }

    internal Lease Acquire(NaturalItemPlacement placement, RuntimeItemDefinition definition,
        ChunkTilemapRenderer owner)
    {
        if (disposed) throw new ObjectDisposedException(nameof(SharedNaturalEntityRegistry));
        if (!placement.IsShared || !definition.UsesResourceEntities || owner == null)
            throw new ArgumentException("共享资源引用缺少有效实体或表现宿主。");
        using var scope = EnterWorld();
        Int2 anchor = placement.SharedAnchor.Value;
        Vector3 position = new(anchor.X + .5f, anchor.Y + .5f, 0f);
        RuntimeWorldAddress address = manager.ResolveWorldAddress(position);
        if (manager.IsNaturalItemRemoved(address, placement.Guid)) return null;
        var key = (address, placement.Guid);
        if (!entries.TryGetValue(key, out Entry entry))
        {
            manager.TryGetNaturalItemOverride(address, placement.Guid, out ItemData saved);
            if (!NaturalEntityEcsService.TryRegister(definition, placement.Guid, position, 0f, 0f,
                address.DimensionId, saved, out NaturalEntityHandle handle))
                throw new InvalidOperationException($"共享资源 {definition.Id} 注册失败。");
            entry = new Entry { Address = address, Anchor = anchor, Guid = placement.Guid,
                ItemId = definition.Id, Handle = handle };
            try
            {
                entries.Add(key, entry);
                NaturalEntityEcsService.SetGeneratedNaturalEntitySourceIdentity(handle, worldKey,
                    address.ChunkOrigin.X, address.ChunkOrigin.Y);
                Entry captured = entry;
                NaturalEntityEcsService.BindRemovalHandler(handle, _ => OnEntityDied(captured));
            }
            catch
            {
                if (entries.TryGetValue(key, out Entry current) && ReferenceEquals(current, entry)) entries.Remove(key);
                NaturalEntityEcsService.Remove(handle);
                throw;
            }
        }
        else if (!entry.Anchor.Equals(anchor) || entry.ItemId != definition.Id)
            throw new InvalidOperationException($"共享自然物身份冲突：{placement.Guid}，禁止覆盖另一实体。");
        if (entry.Removed || !NaturalEntityEcsService.Contains(entry.Handle)) return null;
        var lease = new Lease { Registry = this, Entry = entry, Owner = owner };
        entry.Owners.Add(lease);
        try
        {
            if (entry.Owners.Primary == lease) NaturalEntityEcsService.BindPresentation(entry.Handle, owner);
        }
        catch { Release(lease, false); throw; }
        return lease;
    }

    private GameplayWorldRegistry.WorldExecutionScope EnterWorld() => !string.IsNullOrEmpty(worldKey) && GameplayWorldRegistry.TryGet(worldKey, out _)
        ? GameplayWorldRegistry.UseWorld(worldKey) : default;

    #endregion

    #region 表现交接与持久化

    internal bool IsPrimaryLease(Lease lease) => IsLive(lease) && lease.Entry.Owners.Primary == lease;

    internal void Refresh(Lease lease)
    {
        if (!IsLive(lease) || lease.Entry.Owners.Primary != lease) return;
        using var scope = EnterWorld();
        Entry entry = lease.Entry;
        if (GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(entry.ItemId, out var definition))
        {
            if (!NaturalEntityEcsService.TryRefreshDefinition(entry.Handle, definition))
                Debug.LogError($"共享资源 {entry.ItemId} 配置刷新失败，保留原实体。");
            NaturalEntityEcsService.BindPresentation(entry.Handle, lease.Owner);
        }
    }

    internal void Capture(Lease lease)
    {
        if (!IsLive(lease) || lease.Entry.Owners.Primary != lease || !GameNetwork.HasStateAuthority) return;
        using var scope = EnterWorld();
        Entry entry = lease.Entry;
        NaturalEntityEcsService.PrepareForCapture(entry.Handle);
        if (!entry.Removed && !manager.IsNaturalItemRemoved(entry.Address, entry.Guid) &&
            NaturalEntityEcsService.TryCapture(entry.Handle, out ItemData snapshot))
            manager.CaptureNaturalItemState(entry.Address, snapshot);
    }

    internal void Release(Lease lease, bool capture)
    {
        if (lease == null || lease.Released || lease.Registry != this) return;
        using var scope = EnterWorld();
        Entry entry = lease.Entry;
        bool wasPrimary = entry.Owners.Primary == lease;
        lease.Released = true;
        entry.Owners.Remove(lease);
        if (entry.Owners.Count > 0)
        {
            if (wasPrimary && !entry.Removed && NaturalEntityEcsService.Contains(entry.Handle))
                NaturalEntityEcsService.BindPresentation(entry.Handle, entry.Owners.Primary.Owner);
            return;
        }
        try
        {
            if (capture && !disposed && !entry.Removed && GameNetwork.HasStateAuthority &&
                !manager.IsWorldRuntimeShuttingDown && !manager.IsNaturalItemRemoved(entry.Address, entry.Guid))
            {
                NaturalEntityEcsService.PrepareForCapture(entry.Handle);
                if (!entry.Removed && NaturalEntityEcsService.TryTakeSnapshotAndRemove(entry.Handle, out ItemData snapshot))
                    manager.CaptureNaturalItemState(entry.Address, snapshot);
            }
        }
        finally
        {
            NaturalEntityEcsService.Remove(entry.Handle);
            entries.Remove((entry.Address, entry.Guid));
        }
    }

    private bool IsLive(Lease lease) => !disposed && lease != null && !lease.Released &&
        lease.Registry == this && !lease.Entry.Removed && NaturalEntityEcsService.Contains(lease.Entry.Handle);

    private void OnEntityDied(Entry entry)
    {
        if (entry.Removed) return;
        entry.Removed = true;
        if (!disposed && manager != null && !manager.IsWorldRuntimeShuttingDown && GameNetwork.HasStateAuthority)
        {
            using var scope = EnterWorld();
            manager.MarkNaturalItemRemoved(entry.Address, entry.Guid);
        }
    }

    private void OnRemoved(RuntimeWorldAddress address, int guid)
    {
        if (!entries.TryGetValue((address, guid), out var entry)) return;
        entry.Removed = true;
        NaturalEntityEcsService.Remove(entry.Handle);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (manager != null) manager.NaturalItemRemoved -= OnRemoved;
        foreach (Entry entry in entries.Values)
        {
            entry.Removed = true;
            NaturalEntityEcsService.Remove(entry.Handle);
            entry.Owners.Clear();
        }
        entries.Clear();
    }

    #endregion
}
