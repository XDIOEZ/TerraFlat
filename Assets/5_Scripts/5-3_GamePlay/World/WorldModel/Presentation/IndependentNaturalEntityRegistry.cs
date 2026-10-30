using System;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>可动自然资源由世界持有，区块只借表现，出生点与当前空间归属分开维护。</summary>
internal sealed class IndependentNaturalEntityRegistry : IDisposable
{
    #region 世界身份与当前位置索引

    private sealed class Entry
    {
        internal IndependentNaturalEntitySaveData Saved;
        internal RuntimeWorldAddress SourceAddress, CurrentAddress;
        internal NaturalEntityHandle Handle;
    }
    private readonly struct PresentationBinding
    {
        internal readonly ChunkRuntime Chunk;
        internal readonly ChunkTilemapRenderer Owner;
        internal PresentationBinding(ChunkRuntime chunk, ChunkTilemapRenderer owner) { Chunk = chunk; Owner = owner; }
    }
    private readonly ChunkMgr manager;
    private readonly string worldKey;
    private readonly Dictionary<(string World, int X, int Y, int Guid), Entry> entries = new();
    private readonly Dictionary<NaturalEntityHandle, Entry> activeEntries = new();
    private readonly Dictionary<RuntimeWorldAddress, HashSet<Entry>> currentChunks = new();
    private readonly Dictionary<RuntimeWorldAddress, PresentationBinding> presentations = new();
    private readonly List<Entry> scratch = new();
    private bool disposed;

    internal IndependentNaturalEntityRegistry(ChunkMgr manager)
    {
        this.manager = manager;
        worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        EcologyWorldSaveData ecology = GetEcology();
        if (ecology?.IndependentEntities != null)
            foreach (IndependentNaturalEntitySaveData saved in ecology.IndependentEntities)
            {
                if (saved?.Snapshot?.transform == null || saved.Guid == 0 || saved.Snapshot.Guid != saved.Guid ||
                    string.IsNullOrWhiteSpace(saved.SourceWorldKey))
                    throw new InvalidOperationException("世界自然资源存档缺少完整的出生身份或实体快照。");
                var key = (saved.SourceWorldKey, saved.SourceChunkX, saved.SourceChunkY, saved.Guid);
                if (entries.ContainsKey(key)) throw new InvalidOperationException($"自然物源身份重复：{key}");
                var entry = new Entry { Saved = saved,
                    SourceAddress = CreateSourceAddress(saved.SourceWorldKey, saved.SourceChunkX, saved.SourceChunkY),
                    CurrentAddress = manager.ResolveWorldAddress(saved.Snapshot.transform.position) };
                entries.Add(key, entry);
                AddCurrentChunk(entry);
            }
        manager.NaturalItemRemoved += HandleSourceRemoved;
        NaturalEntityEcsService.PhysicsPoseCommitted += HandlePhysicsPoseCommitted;
    }

    private EcologyWorldSaveData GetEcology() => GetWorldEcology(worldKey);

    private static EcologyWorldSaveData GetWorldEcology(string key)
    {
        var planets = SaveDataMgr.Instance?.SaveData?.PlanetData_Dict;
        if (planets == null || string.IsNullOrWhiteSpace(key) ||
            !planets.TryGetValue(key, out PlanetData planet) || planet == null) return null;
        return planet.Ecology ??= new EcologyWorldSaveData();
    }

    private static RuntimeWorldAddress CreateSourceAddress(string sourceWorldKey, int x, int y)
        => new(global::WorldAddress.FromWorldKey(sourceWorldKey).DimensionId, new Int2(x, y));

    private GameplayWorldRegistry.WorldExecutionScope EnterWorld() => !string.IsNullOrEmpty(worldKey) && GameplayWorldRegistry.TryGet(worldKey, out _)
        ? GameplayWorldRegistry.UseWorld(worldKey) : default;

    private void AddCurrentChunk(Entry entry)
    {
        if (!currentChunks.TryGetValue(entry.CurrentAddress, out HashSet<Entry> bucket))
            currentChunks.Add(entry.CurrentAddress, bucket = new HashSet<Entry>());
        bucket.Add(entry);
    }

    private void RemoveCurrentChunk(Entry entry)
    {
        if (!currentChunks.TryGetValue(entry.CurrentAddress, out HashSet<Entry> bucket)) return;
        bucket.Remove(entry);
        if (bucket.Count == 0) currentChunks.Remove(entry.CurrentAddress);
    }

    #endregion

    #region 正式生成与区块表现借用

    internal void BindChunk(ChunkRuntime chunk, ChunkTilemapRenderer owner)
    {
        if (disposed || chunk == null || owner == null) return;
        using var scope = EnterWorld();
        presentations[chunk.Address] = new PresentationBinding(chunk, owner);
        if (!currentChunks.TryGetValue(chunk.Address, out HashSet<Entry> bucket)) return;
        scratch.Clear(); scratch.AddRange(bucket);
        foreach (Entry entry in scratch) ActivateOrPresent(entry);
        scratch.Clear();
    }

    // 迁移的目标表现和原生阻挡必须绑定同一世界及区块。
    private bool HasBoundChunkPhysicsBackend(PresentationBinding binding)
    {
        return binding.Chunk != null && GameplayWorldRegistry.TryGetPhysics(worldKey, out var physics) &&
            physics.IsValid && GameplayWorldRegistry.TryGetTerrainPhysicsSource(worldKey, out var terrain) &&
            terrain.IsBound(binding.Chunk);
    }

    internal bool AcquireGeneratedEntity(NaturalItemPlacement placement, RuntimeItemDefinition definition,
        RuntimeWorldAddress address, Vector3 position, float baseline, float precipitation, ItemData persisted)
    {
        using var scope = EnterWorld();
        if (placement.IsShared)
        {
            var anchor = placement.SharedAnchor.Value;
            position = new Vector3(anchor.X + .5f, anchor.Y + .5f);
            address = manager.ResolveWorldAddress(position);
        }
        var key = (worldKey, address.ChunkOrigin.X, address.ChunkOrigin.Y, placement.Guid);
        if (entries.TryGetValue(key, out Entry existing))
        {
            ActivateOrPresent(existing);
            return true;
        }
        if (!NaturalEntityEcsService.UsesIndependentPhysicsProxy(definition)) return false;
        EcologyWorldSaveData ecology = GetEcology() ?? throw new InvalidOperationException("世界生态存档尚未准备好。");
        if (!NaturalEntityEcsService.TryRegister(definition, placement.Guid, position, baseline, precipitation,
            address.DimensionId, persisted, out NaturalEntityHandle handle))
            throw new InvalidOperationException($"独立自然物 {definition.Id} 生成失败。");
        Entry entry = null;
        try
        {
            if (!NaturalEntityEcsService.TryCapture(handle, out ItemData snapshot))
                throw new InvalidOperationException("独立自然物未能取得初始快照。");
            var saved = new IndependentNaturalEntitySaveData
            { SourceWorldKey = key.Item1, SourceChunkX = key.Item2, SourceChunkY = key.Item3, Guid = placement.Guid, Snapshot = snapshot };
            entry = new Entry { Saved = saved, SourceAddress = address,
                CurrentAddress = manager.ResolveWorldAddress(snapshot.transform.position), Handle = handle };
            entries.Add(key, entry); activeEntries.Add(handle, entry); AddCurrentChunk(entry);
            ecology.IndependentEntities.Add(saved);
            AttachEntityOwnership(entry);
            PresentOrSuspend(entry);
            return true;
        }
        catch
        {
            if (entry != null) Forget(entry, true); else NaturalEntityEcsService.Remove(handle);
            throw;
        }
    }

    internal bool ContainsGeneratedSourceIdentity(RuntimeWorldAddress address, NaturalItemPlacement placement)
    {
        if (placement.IsShared)
        {
            var anchor = placement.SharedAnchor.Value;
            address = manager.ResolveWorldAddress(new Vector3(anchor.X + .5f, anchor.Y + .5f));
        }
        return entries.ContainsKey((worldKey, address.ChunkOrigin.X, address.ChunkOrigin.Y, placement.Guid));
    }

    internal bool TryGetGeneratedEntityHandle(RuntimeWorldAddress sourceAddress, int guid, out NaturalEntityHandle handle)
    {
        handle = default;
        if (!entries.TryGetValue((worldKey, sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y, guid), out Entry entry)) return false;
        handle = entry.Handle;
        return NaturalEntityEcsService.Contains(handle);
    }

    private void ActivateOrPresent(Entry entry)
    {
        if (!presentations.ContainsKey(entry.CurrentAddress)) return;
        if (!NaturalEntityEcsService.Contains(entry.Handle))
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null || !resources.TryGetItemDefinition(entry.Saved.Snapshot.IDName, out RuntimeItemDefinition definition)) return;
            PresentationBinding binding = presentations[entry.CurrentAddress];
            Vector3 position = entry.Saved.Snapshot.transform.position;
            int localX = Mathf.Clamp(Mathf.FloorToInt(position.x) - entry.CurrentAddress.ChunkOrigin.X, 0, binding.Chunk.Terrain.Width - 1);
            int localY = Mathf.Clamp(Mathf.FloorToInt(position.y) - entry.CurrentAddress.ChunkOrigin.Y, 0, binding.Chunk.Terrain.Height - 1);
            binding.Chunk.Terrain.TryGetEnvironmentValue("temperature.celsius", localX, localY, out float baseline);
            binding.Chunk.Terrain.TryGetEnvironmentValue("precipitation", localX, localY, out float precipitation);
            if (!NaturalEntityEcsService.TryRegister(definition, entry.Saved.Guid, position, baseline, precipitation,
                entry.CurrentAddress.DimensionId, entry.Saved.Snapshot, out NaturalEntityHandle handle))
                throw new InvalidOperationException($"独立自然物 {definition.Id} 恢复失败。");
            try
            {
                entry.Handle = handle;
                activeEntries.Add(handle, entry);
                AttachEntityOwnership(entry);
            }
            catch
            {
                activeEntries.Remove(handle);
                entry.Handle = default;
                NaturalEntityEcsService.Remove(handle);
                throw;
            }
        }
        PresentOrSuspend(entry);
    }

    private void AttachEntityOwnership(Entry entry)
    {
        NaturalEntityEcsService.SetIndependentPhysicsSourceIdentity(entry.Handle,
            entry.Saved.SourceWorldKey, entry.Saved.SourceChunkX, entry.Saved.SourceChunkY);
        NaturalEntityEcsService.BindRemovalHandler(entry.Handle, _ => HandleEntityDied(entry));
    }

    private void PresentOrSuspend(Entry entry)
    {
        if (!NaturalEntityEcsService.Contains(entry.Handle)) return;
        if (presentations.TryGetValue(entry.CurrentAddress, out PresentationBinding binding))
            NaturalEntityEcsService.BindPresentation(entry.Handle, binding.Owner);
        else Suspend(entry);
    }

    internal void UnbindChunk(ChunkRuntime chunk, ChunkTilemapRenderer owner)
    {
        if (disposed || chunk == null || !presentations.TryGetValue(chunk.Address, out var binding) || binding.Owner != owner) return;
        using var scope = EnterWorld();
        presentations.Remove(chunk.Address);
        if (!currentChunks.TryGetValue(chunk.Address, out var bucket)) return;
        scratch.Clear(); scratch.AddRange(bucket);
        foreach (Entry entry in scratch) Suspend(entry);
        scratch.Clear();
    }

    // 重建区块表现时也刷新由世界持有的资源，物理模式按实例延后切换。
    internal void RefreshPresentedEntitiesIn(ChunkRuntime chunk)
    {
        if (disposed || chunk == null || !currentChunks.TryGetValue(chunk.Address, out var bucket)) return;
        using var scope = EnterWorld();
        scratch.Clear(); scratch.AddRange(bucket);
        foreach (Entry entry in scratch)
        {
            if (!NaturalEntityEcsService.Contains(entry.Handle) || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(entry.Saved.Snapshot.IDName, out var definition)) continue;
            if (!NaturalEntityEcsService.TryRefreshDefinition(entry.Handle, definition))
                Debug.LogError($"资源实体 {definition.Id} 配置刷新失败，保留原实体。");
            PresentOrSuspend(entry);
        }
        scratch.Clear();
    }

    internal int CountPresentedIn(ChunkRuntime chunk)
    {
        if (chunk == null || !currentChunks.TryGetValue(chunk.Address, out var bucket)) return 0;
        int count = 0;
        foreach (Entry entry in bucket) if (NaturalEntityEcsService.Contains(entry.Handle)) count++;
        return count;
    }

    #endregion

    #region 物理归属交接与保存释放

    private void HandlePhysicsPoseCommitted(NaturalEntityHandle handle, Vector2 previous, Vector2 position,
        WorldPhysicsEntityPose2D pose)
    {
        if (disposed || !activeEntries.TryGetValue(handle, out Entry entry)) return;
        using var scope = EnterWorld();
        entry.Saved.Snapshot.transform.position = position;
        entry.Saved.Snapshot.transform.rotation = Quaternion.Euler(0f, 0f, pose.RotationDegrees);
        // 客户端刚体不预测，保存态直接使用权威速度。
        entry.Saved.Snapshot.PhysicsState ??= new ItemPhysicsRuntimeState();
        entry.Saved.Snapshot.PhysicsState.Velocity = pose.Velocity;
        entry.Saved.Snapshot.PhysicsState.AngularVelocity = pose.AngularVelocity;
        RuntimeWorldAddress current = manager.ResolveWorldAddress(position);
        if (entry.CurrentAddress == current) return;
        RemoveCurrentChunk(entry);
        entry.CurrentAddress = current;
        AddCurrentChunk(entry);
        PresentOrSuspend(entry);
    }

    // 先准备目标实体，失败时恢复原实体与原租约，成功后才撤销源所有权。
    internal bool TryTransferNaturalEntityToWorld(NaturalEntityHandle handle, IndependentNaturalEntityRegistry target,
        Vector2 targetPosition, string sourceWorldKey, int sourceX, int sourceY, int guid, bool independentSource,
        out NaturalEntityHandle targetHandle, out string failure)
    {
        targetHandle = default;
        failure = null;
        using var sourceScope = EnterWorld();
        if (disposed || target == null || target.disposed || target == this || GetEcology() == null ||
            GetWorldEcology(sourceWorldKey) == null || !NaturalEntityEcsService.TryGetPhysicsProxy(handle, out var sourceProxy))
        { failure = "源世界或目标世界的自然资源所有权未就绪。"; return false; }
        var sourceKey = (sourceWorldKey, sourceX, sourceY, guid);
        activeEntries.TryGetValue(handle, out Entry sourceEntry);
        if (independentSource && (sourceEntry == null ||
            (sourceEntry.Saved.SourceWorldKey, sourceEntry.Saved.SourceChunkX, sourceEntry.Saved.SourceChunkY, sourceEntry.Saved.Guid) != sourceKey))
        { failure = "源资源登记与实体身份不一致。"; return false; }
        if (!independentSource && (sourceEntry != null || sourceWorldKey != worldKey))
        { failure = "固定资源没有可交接的天然生成归属。"; return false; }
        RuntimeWorldAddress sourceAddress = CreateSourceAddress(sourceWorldKey, sourceX, sourceY);
        if (sourceWorldKey == worldKey && manager.IsNaturalItemRemoved(sourceAddress, guid))
        { failure = "源生成点已经移除，不能重复迁移。"; return false; }
        if (sourceWorldKey != worldKey && sourceEntry == null)
        { failure = "迁入资源缺少当前世界的所有权记录。"; return false; }
        if (independentSource)
        {
            Vector2 actualPosition = WorldLocalPresentation.ToLogical(sourceProxy.CapturePose().Position);
            if (!presentations.ContainsKey(manager.ResolveWorldAddress(actualPosition)))
            { failure = "源资源已到达未准备表现的区块，请先完成该位置的生命周期交接。"; return false; }
        }

        Entry candidate = null;
        bool sourceDetached = false;
        bool paused = false;
        ItemData snapshot = null;
        Vector2 sourcePosition = default;
        try
        {
            using (target.EnterWorld())
            {
                RuntimeWorldAddress targetAddress = target.manager.ResolveWorldAddress(targetPosition);
                if (!target.presentations.TryGetValue(targetAddress, out PresentationBinding targetBinding) ||
                    !target.HasBoundChunkPhysicsBackend(targetBinding) || target.GetEcology() == null ||
                    target.entries.ContainsKey(sourceKey) ||
                    NaturalEntityEcsService.HasGeneratedNaturalEntitySourceInCurrentWorld(sourceWorldKey, sourceX, sourceY, guid))
                { failure = "目标区块表现或自然资源身份尚未准备好。"; return false; }
            }
            sourceProxy.SetParticipationInWorldPhysics(false);
            paused = true;
            NaturalEntityEcsService.PrepareForCapture(handle);
            if (!NaturalEntityEcsService.TryCapture(handle, out snapshot) || snapshot?.transform == null)
                throw new InvalidOperationException("源资源未能取得迁移快照。");
            sourcePosition = snapshot.transform.position;
            if (!target.TryPrepareTransferredEntity(sourceWorldKey, sourceX, sourceY, guid, targetPosition,
                snapshot, out candidate, out failure)) return false;

            if (sourceWorldKey == worldKey)
            {
                if (!manager.TryRemoveNaturalItemForWorldTransfer(sourceAddress, guid))
                    throw new InvalidOperationException("源生成点未能提交迁出。");
            }
            else
                Forget(sourceEntry, false);
            sourceDetached = true;
            NaturalEntityEcsService.RemoveNaturalEntityReservedForWorldTransfer(handle);

            PublishCompletedWorldTransfer(target, candidate, sourceWorldKey, sourceX, sourceY, guid, sourcePosition);
            targetHandle = candidate.Handle;
            return true;
        }
        catch (Exception exception)
        {
            // 源已撤销时保留已准备好的目标，不能把提交结果误报为失败。
            if (candidate != null && (sourceDetached || !NaturalEntityEcsService.Contains(handle)))
            {
                sourceDetached = true;
                targetHandle = candidate.Handle;
                Debug.LogException(exception);
                try { NaturalEntityEcsService.RemoveNaturalEntityReservedForWorldTransfer(handle); }
                catch (Exception cleanupException) { Debug.LogException(cleanupException); }
                try { PublishCompletedWorldTransfer(target, candidate, sourceWorldKey, sourceX, sourceY, guid, sourcePosition); }
                catch (Exception notificationException) { Debug.LogException(notificationException); }
                return true;
            }
            failure = exception.Message;
            return false;
        }
        finally
        {
            if (sourceDetached && candidate != null)
                NaturalEntityEcsService.ReleaseNaturalEntityWorldTransferReservation(candidate.Handle);
            if (!sourceDetached)
            {
                try
                {
                    if (candidate != null)
                        using (target.EnterWorld()) target.Forget(candidate, true, releaseReservedEntity: true);
                }
                catch (Exception cleanupException) { Debug.LogException(cleanupException); }
                try
                {
                    if (sourceWorldKey == worldKey && snapshot != null &&
                        GetEcology()?.IsRemoved(sourceX, sourceY, guid) == true)
                        GetEcology().CaptureChangedItem(sourceX, sourceY, snapshot);
                }
                finally
                {
                    if (paused && sourceProxy.IsValid) sourceProxy.SetParticipationInWorldPhysics(true);
                }
            }
        }
    }

    private void PublishCompletedWorldTransfer(IndependentNaturalEntityRegistry target, Entry candidate,
        string sourceWorldKey, int sourceX, int sourceY, int guid, Vector2 sourcePosition)
    {
        // 两边独立冲刷合批阻挡，单侧异常不能阻断另一侧及网络通知。
        try { using (EnterWorld()) if (GameplayWorldRegistry.TryGetPhysics(worldKey, out var physics)) physics.FlushPendingGeometry(); }
        catch (Exception exception) { Debug.LogException(exception); }
        try { using (target.EnterWorld()) if (GameplayWorldRegistry.TryGetPhysics(target.worldKey, out var physics)) physics.FlushPendingGeometry(); }
        catch (Exception exception) { Debug.LogException(exception); }
        try { Physics2D.SyncTransforms(); }
        catch (Exception exception) { Debug.LogException(exception); }
        if (sourceWorldKey == target.worldKey && target.GetEcology()?.TryGetChunk(sourceX, sourceY, out var originChunk) == true)
        {
            originChunk.RemovedGuids?.Remove(guid);
            originChunk.RenewalYears?.Remove(guid);
        }
        NaturalEntityEcsService.PublishIndependentPhysicsState(worldKey, sourceWorldKey, sourceX, sourceY,
            guid, sourcePosition, null);
        using (target.EnterWorld())
            NaturalEntityEcsService.PublishIndependentPhysicsStateSnapshot(candidate.Handle, candidate.Saved.Snapshot);
    }

    private bool TryPrepareTransferredEntity(string sourceWorldKey, int sourceX, int sourceY, int guid,
        Vector2 position, ItemData snapshot, out Entry candidate, out string failure)
    {
        candidate = null;
        failure = null;
        using var scope = EnterWorld();
        Vector2 normalized = WorldTopologyRuntime.NormalizePosition(position);
        RuntimeWorldAddress current = manager.ResolveWorldAddress(normalized);
        var key = (sourceWorldKey, sourceX, sourceY, guid);
        if (!presentations.TryGetValue(current, out PresentationBinding binding) || !HasBoundChunkPhysicsBackend(binding) ||
            entries.ContainsKey(key) || NaturalEntityEcsService.HasGeneratedNaturalEntitySourceInCurrentWorld(sourceWorldKey, sourceX, sourceY, guid))
        { failure = "目标区块表现或自然资源身份不可用。"; return false; }
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(snapshot.IDName, out RuntimeItemDefinition definition) ||
            !definition.UsesResourceEntities)
        { failure = "目标资源定义尚未准备好。"; return false; }
        int localX = Mathf.Clamp(Mathf.FloorToInt(normalized.x) - current.ChunkOrigin.X, 0, binding.Chunk.Terrain.Width - 1);
        int localY = Mathf.Clamp(Mathf.FloorToInt(normalized.y) - current.ChunkOrigin.Y, 0, binding.Chunk.Terrain.Height - 1);
        binding.Chunk.Terrain.TryGetEnvironmentValue("temperature.celsius", localX, localY, out float baseline);
        binding.Chunk.Terrain.TryGetEnvironmentValue("precipitation", localX, localY, out float precipitation);
        ItemData destinationSnapshot = NaturalEntityEcsService.CloneNaturalEntitySnapshotPreservingMotion(snapshot);
        destinationSnapshot.transform.position = new Vector3(normalized.x, normalized.y, destinationSnapshot.transform.position.z);
        if (!NaturalEntityEcsService.TryRegisterTransferredNaturalEntity(definition, guid, destinationSnapshot.transform.position, baseline, precipitation,
            current.DimensionId, destinationSnapshot, out NaturalEntityHandle handle))
        { failure = "目标自然资源未能注册。"; return false; }
        var saved = new IndependentNaturalEntitySaveData
        { SourceWorldKey = sourceWorldKey, SourceChunkX = sourceX, SourceChunkY = sourceY, Guid = guid, Snapshot = destinationSnapshot };
        candidate = new Entry { Saved = saved, SourceAddress = CreateSourceAddress(sourceWorldKey, sourceX, sourceY),
            CurrentAddress = current, Handle = handle };
        try
        {
            entries.Add(key, candidate);
            activeEntries.Add(handle, candidate);
            AddCurrentChunk(candidate);
            GetEcology().IndependentEntities.Add(saved);
            AttachEntityOwnership(candidate);
            NaturalEntityEcsService.BindPresentation(handle, binding.Owner);
            if (!NaturalEntityEcsService.TryCapture(handle, out ItemData prepared))
                throw new InvalidOperationException("目标资源未能取得完整状态。");
            candidate.Saved.Snapshot = prepared;
            return true;
        }
        catch
        {
            Forget(candidate, true, releaseReservedEntity: true);
            candidate = null;
            throw;
        }
    }

    private void Suspend(Entry entry)
    {
        NaturalEntityHandle handle = entry.Handle;
        if (!NaturalEntityEcsService.Contains(handle)) { activeEntries.Remove(handle); entry.Handle = default; return; }
        NaturalEntityEcsService.PrepareForCapture(handle);
        if (NaturalEntityEcsService.TryCapture(handle, out ItemData finalSnapshot))
            NaturalEntityEcsService.PublishIndependentPhysicsStateSnapshot(handle, finalSnapshot);
        if (NaturalEntityEcsService.TryTakeSnapshotAndRemove(handle, out ItemData snapshot)) entry.Saved.Snapshot = snapshot;
        else NaturalEntityEcsService.Remove(handle);
        activeEntries.Remove(handle);
        entry.Handle = default;
    }

    internal void CaptureAll()
    {
        if (disposed || !GameNetwork.HasStateAuthority) return;
        using var scope = EnterWorld();
        scratch.Clear(); scratch.AddRange(activeEntries.Values);
        foreach (Entry entry in scratch)
        {
            NaturalEntityEcsService.PrepareForCapture(entry.Handle);
            if (NaturalEntityEcsService.TryCapture(entry.Handle, out ItemData snapshot)) entry.Saved.Snapshot = snapshot;
        }
        scratch.Clear();
    }

    internal void ApplyNetworkPose(NaturalEntityPhysicsNetworkPose pose)
    {
        if (disposed || GameNetwork.HasStateAuthority ||
            !entries.TryGetValue((pose.SourceWorldKey, pose.SourceChunkX, pose.SourceChunkY, pose.Guid), out Entry entry)) return;
        if (NaturalEntityEcsService.Contains(entry.Handle) &&
            NaturalEntityEcsService.ApplyIndependentPhysicsNetworkPoseToEntity(entry.Handle, pose)) return;
        entry.Saved.Snapshot.transform.position = pose.Position;
        entry.Saved.Snapshot.transform.rotation = Quaternion.Euler(0f, 0f, pose.RotationDegrees);
        entry.Saved.Snapshot.PhysicsState ??= new ItemPhysicsRuntimeState();
        entry.Saved.Snapshot.PhysicsState.Velocity = pose.Velocity;
        entry.Saved.Snapshot.PhysicsState.AngularVelocity = pose.AngularVelocity;
        RuntimeWorldAddress next = manager.ResolveWorldAddress(pose.Position);
        if (entry.CurrentAddress != next)
        { RemoveCurrentChunk(entry); entry.CurrentAddress = next; AddCurrentChunk(entry); }
        ActivateOrPresent(entry);
    }

    internal void ApplyNetworkState(string sourceWorldKey, int sourceX, int sourceY, int guid, ItemData snapshot)
    {
        if (disposed || GameNetwork.HasStateAuthority) return;
        var key = (sourceWorldKey, sourceX, sourceY, guid);
        entries.TryGetValue(key, out Entry entry);
        if (snapshot == null)
        {
            if (entry != null) Forget(entry, true);
            if (sourceWorldKey == worldKey)
            {
                GetEcology()?.MarkRemoved(sourceX, sourceY, guid);
                manager.NotifyNaturalItemRemovedToWorldOwners(CreateSourceAddress(sourceWorldKey, sourceX, sourceY), guid);
            }
            return;
        }
        if (snapshot.Guid != guid || snapshot.transform == null) return;
        if (entry == null)
        {
            var saved = new IndependentNaturalEntitySaveData
            { SourceWorldKey = sourceWorldKey, SourceChunkX = sourceX, SourceChunkY = sourceY, Guid = guid, Snapshot = snapshot };
            entry = new Entry { Saved = saved,
                SourceAddress = CreateSourceAddress(sourceWorldKey, sourceX, sourceY),
                CurrentAddress = manager.ResolveWorldAddress(snapshot.transform.position) };
            entries.Add(key, entry); AddCurrentChunk(entry);
            GetEcology()?.IndependentEntities.Add(saved);
        }
        else
        {
            entry.Saved.Snapshot = snapshot;
            RuntimeWorldAddress current = manager.ResolveWorldAddress(snapshot.transform.position);
            if (entry.CurrentAddress != current)
            { RemoveCurrentChunk(entry); entry.CurrentAddress = current; AddCurrentChunk(entry); }
            if (NaturalEntityEcsService.Contains(entry.Handle))
            {
                NaturalEntityEcsService.ApplySnapshot(entry.Handle, snapshot, float.NaN);
                PresentOrSuspend(entry);
            }
        }
        if (sourceWorldKey == worldKey && GetEcology()?.TryGetChunk(sourceX, sourceY, out var originChunk) == true)
        {
            originChunk.RemovedGuids?.Remove(guid);
            originChunk.RenewalYears?.Remove(guid);
        }
        ActivateOrPresent(entry);
    }

    private void HandleEntityDied(Entry entry)
    {
        if (disposed || !entries.ContainsKey((entry.Saved.SourceWorldKey, entry.Saved.SourceChunkX, entry.Saved.SourceChunkY, entry.Saved.Guid))) return;
        bool renews = NaturalEntityEcsService.TryGetRenewalYear(entry.Handle, out int year);
        Forget(entry, removeEntity: false);
        if (!manager.IsWorldRuntimeShuttingDown && GameNetwork.HasStateAuthority)
        {
            if (entry.Saved.SourceWorldKey == worldKey)
            {
                manager.MarkNaturalItemRemoved(entry.SourceAddress, entry.Saved.Guid);
                if (renews) manager.ScheduleNaturalRenewal(entry.SourceAddress, entry.Saved.Guid, year);
            }
            else
            {
                EcologyWorldSaveData origin = GetWorldEcology(entry.Saved.SourceWorldKey);
                origin?.MarkRemoved(entry.Saved.SourceChunkX, entry.Saved.SourceChunkY, entry.Saved.Guid);
                if (renews && origin != null && DayTimeSystem.Instance != null &&
                    DayTimeSystem.Instance.TryGetResolvedTimeData(entry.Saved.SourceWorldKey, out _, out TimeData originTime))
                {
                    year = SeasonCalendar.Sample(originTime).Year + 1;
                    origin.GetOrCreateChunk(entry.Saved.SourceChunkX, entry.Saved.SourceChunkY).RenewalYears[entry.Saved.Guid] = year;
                }
            }
        }
    }

    private void HandleSourceRemoved(RuntimeWorldAddress address, int guid)
    {
        if (entries.TryGetValue((worldKey, address.ChunkOrigin.X, address.ChunkOrigin.Y, guid), out Entry entry)) Forget(entry, true);
    }

    private void Forget(Entry entry, bool removeEntity, bool releaseReservedEntity = false)
    {
        var key = (entry.Saved.SourceWorldKey, entry.Saved.SourceChunkX, entry.Saved.SourceChunkY, entry.Saved.Guid);
        if (entries.TryGetValue(key, out Entry current) && ReferenceEquals(current, entry)) entries.Remove(key);
        activeEntries.Remove(entry.Handle);
        RemoveCurrentChunk(entry);
        GetEcology()?.IndependentEntities.Remove(entry.Saved);
        if (removeEntity)
        {
            if (releaseReservedEntity) NaturalEntityEcsService.RemoveNaturalEntityReservedForWorldTransfer(entry.Handle);
            else NaturalEntityEcsService.Remove(entry.Handle);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        using var scope = EnterWorld();
        CaptureAll();
        disposed = true;
        manager.NaturalItemRemoved -= HandleSourceRemoved;
        NaturalEntityEcsService.PhysicsPoseCommitted -= HandlePhysicsPoseCommitted;
        foreach (Entry entry in entries.Values) NaturalEntityEcsService.Remove(entry.Handle);
        entries.Clear(); activeEntries.Clear(); currentChunks.Clear(); presentations.Clear(); scratch.Clear();
    }

    #endregion
}
