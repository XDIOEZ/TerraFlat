using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ItemMgr
{
    #region 运行时物品替换

    private sealed class RuntimeItemReplacement
    {
        public Item Original;
        public ItemData OriginalData;
        public string OriginalDefinitionId;
        public uint OriginalGeneration;
        public ulong OriginalToken;
        public int Guid;
        public Item Owner;
        public Transform Parent;
        public int SiblingIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LocalScale;
        public bool Active;
        public Chunk ChunkOwner;
        public bool WasPersistedInChunk;
        public ChunkNaturalItemRenderer TransientOwner;
        public ItemInstanceSnapshot OriginalState;
        public bool SourceTouched;
        public bool SourceDetached;
        public bool SourceUnloaded;
        public Item Candidate;
        public GameObject CandidateObject;
        public ItemData ReplacementData;
        public ulong CandidateToken;
        public uint? LoadedCandidateGeneration;
    }

    private readonly Dictionary<int, RuntimeItemReplacement> _runtimeItemReplacements = new();

    /// <summary>普通世界物品换定义时交接外壳和运行态；提交失败恢复同一源实例。</summary>
    public Item ReplaceRuntimeItem(Item original, ItemData replacement)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        if (original is Player || original is Map)
            throw new InvalidOperationException("玩家和地图必须通过各自的生命周期入口替换。");
        if (original.DestructionHandled || original.IsInPool || original.IsRuntimeTransitioning || !original.IsInitialized ||
            !_runtimeRegistry.TryGetRegisteredGuid(original, out int guid) || original.itemData?.Guid != guid)
            throw new InvalidOperationException("只有已加载且身份一致的运行时物品可以替换。");
        if (ReferenceEquals(original.itemData, replacement))
            throw new ArgumentException("替换数据必须是独立实例。", nameof(replacement));
        if (string.IsNullOrWhiteSpace(replacement.IDName))
            throw new ArgumentException("替换数据缺少定义 ID。", nameof(replacement));
        if (!original.InHand && MachineWorld.OwnsWorldItem(original.itemData))
            throw new InvalidOperationException("机器世界持有的物品必须通过 MachineWorld 替换。");
        foreach (Module module in original.Mods.Values)
            if (module != null && module.IsRuntimeTransitioning)
                throw new InvalidOperationException("物品模块正在加载或卸载，不能替换外壳。");

        Transform sourceTransform = original.transform;
        var transfer = new RuntimeItemReplacement
        {
            Original = original, OriginalData = original.itemData,
            OriginalDefinitionId = original.itemData.IDName,
            OriginalGeneration = original.RuntimeGeneration,
            OriginalToken = _runtimeRegistry.GetRegistrationToken(original), Guid = guid,
            Owner = original.Owner, Parent = sourceTransform.parent,
            SiblingIndex = sourceTransform.GetSiblingIndex(), Position = sourceTransform.position,
            Rotation = sourceTransform.rotation, LocalScale = sourceTransform.localScale,
            Active = original.gameObject.activeSelf, ReplacementData = replacement
        };
        Chunk chunk = original.GetComponentInParent<Chunk>();
        if (chunk != null && chunk.RunTimeItems.TryGetValue(guid, out Item indexed) && ReferenceEquals(indexed, original))
        {
            transfer.ChunkOwner = chunk;
            transfer.WasPersistedInChunk = chunk.MapSave?.items != null &&
                chunk.MapSave.items.TryGetValue(original.itemData.IDName, out var savedItems) && savedItems.Contains(original.itemData);
        }
        transfer.TransientOwner = original.GetComponentInParent<ChunkNaturalItemRenderer>(true);
        if (transfer.TransientOwner != null && transfer.TransientOwner.TryGetSpawnedItem(guid, out Item natural) &&
            ReferenceEquals(natural, original))
            throw new InvalidOperationException("自然生态持有的实体必须通过生态生命周期入口替换。");
        if (!_runtimeItemReplacements.TryAdd(guid, transfer))
            throw new InvalidOperationException($"Item {guid} 已在进行运行时替换。");
        if (!_runtimeDespawning.Add(original))
        {
            _runtimeItemReplacements.Remove(guid);
            throw new InvalidOperationException($"Item {guid} 已在退出运行态。");
        }

        try
        {
            original.Save();
            EnsureReplacementSourceCurrent(transfer, registered: true);
            transfer.Position = original.transform.position;
            transfer.Rotation = original.transform.rotation;
            transfer.LocalScale = original.transform.localScale;
            transfer.OriginalState = ItemInstanceSnapshot.Capture(transfer.OriginalData);
            PrepareReplacementCandidate(transfer);
            EnsureReplacementSourceCurrent(transfer, registered: true);

            // 先退出源的公共身份和区块，再交接 GUID，旧外壳清理不能误删新体。
            transfer.SourceTouched = true;
            RuntimeItemDespawning?.Invoke(original);
            EnsureReplacementSourceCurrent(transfer, registered: true);
            WorldItemWaterSystem.CancelSpawnCheck(original);
            WorldItemWaterSystem.ClearRuntimeState(original);
            transfer.SourceDetached = true;
            transfer.ChunkOwner?.RemoveItem(original);
            transfer.TransientOwner?.UnregisterTransientItem(original);
            original.transform.SetParent(null, true);
            UnregisterRuntimeItem(original);
            original.gameObject.SetActive(false);
            transfer.SourceUnloaded = true;
            original.ModuleUnload();
            EnsureReplacementSourceCurrent(transfer, registered: false);

            Item candidate = transfer.Candidate;
            RestoreReplacementPose(transfer, candidate);
            RegisterRuntimeItem(candidate, replacement.IDName, out transfer.CandidateToken, transfer);
            AttachReplacementOwnership(transfer, candidate);
            candidate.gameObject.SetActive(transfer.Active);
            EnsureSpawnRegistrationCurrent(candidate, transfer.ReplacementData, transfer.CandidateToken);
            candidate.Load();
            EnsureReplacementCandidateCurrent(transfer);
            uint candidateGeneration = candidate.RuntimeGeneration;
            transfer.LoadedCandidateGeneration = candidateGeneration;
            RuntimeItemInstantiated?.Invoke(candidate);
            EnsureReplacementCandidateCurrent(transfer);
            if (candidate.RuntimeGeneration != candidateGeneration)
                throw new InvalidOperationException("替换候选体在生成回调中切换了运行代际。");
            EnsureReplacementSourceCurrent(transfer, registered: false);
            WorldItemWaterSystem.ScheduleSpawnCheck(candidate);
            FinishReplacedSource(transfer);
            return candidate;
        }
        catch (Exception replacementError)
        {
            if (original != null && !original.DestructionHandled && !original.IsInPool &&
                original.RuntimeGeneration == transfer.OriginalGeneration && ReferenceEquals(original.itemData, transfer.OriginalData) &&
                (!_runtimeRegistry.Contains(original) || _runtimeRegistry.IsRegistrationCurrent(original, transfer.OriginalToken)))
            {
                transfer.OriginalData.Guid = transfer.Guid;
                transfer.OriginalData.IDName = transfer.OriginalDefinitionId;
            }
            CleanupFailedItemSpawn(transfer.Candidate, transfer.CandidateObject,
                transfer.ReplacementData, transfer.CandidateToken, transfer.LoadedCandidateGeneration);
            if (transfer.SourceTouched)
            {
                try { RestoreReplacementSource(transfer); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("运行时物品替换失败，源实例恢复也出现错误。", replacementError, rollbackError);
                }
            }
            throw;
        }
        finally
        {
            _runtimeItemReplacements.Remove(guid);
            _runtimeDespawning.Remove(original);
        }
    }

    private void PrepareReplacementCandidate(RuntimeItemReplacement transfer)
    {
        GameRes resources = GameRes.Instance;
        ItemData data = ItemDefinitionRuntime.RebasePersistedData(resources, transfer.ReplacementData);
        if (resources == null || !resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition))
            throw new InvalidOperationException($"替换目标不存在当前物品定义：{data.IDName}。");
        if (!transfer.OriginalData.inHand && MachineWorld.OwnsWorldItem(data))
            throw new InvalidOperationException($"替换目标 {data.IDName} 必须由 MachineWorld 生成。");
        if (AiRuntimeBackendService.UsesEntities(data.IDName))
            throw new InvalidOperationException($"替换目标 {data.IDName} 必须通过 ECS 生成入口创建。");
        GameObject prefab = resources.GetPrefab(data.IDName, logError: false);
        if (prefab == null) throw new InvalidOperationException($"替换目标缺少外壳：{data.IDName}。");

        data.Guid = transfer.Guid;
        data.inHand = transfer.OriginalData.inHand;
        data.Stack ??= new ItemStack();
        data.transform = new ItemTransform
        {
            position = transfer.OriginalData.transform.position,
            rotation = transfer.Rotation, scale = transfer.LocalScale
        };
        transfer.ReplacementData = data;

        // 在独立停用根下装配，构建异常不会激活或注册半成品。
        GameObject staging = new("ItemReplacementStaging");
        staging.SetActive(false);
        try
        {
            SceneManager.MoveGameObjectToScene(staging, transfer.Original.gameObject.scene);
            transfer.CandidateObject = Instantiate(prefab, staging.transform, false);
            Item candidate = transfer.CandidateObject.GetComponent<Item>();
            transfer.Candidate = candidate;
            if (candidate == null || candidate is Player || candidate is Map)
                throw new InvalidOperationException($"替换目标 {data.IDName} 必须是普通 Item 外壳。");
            candidate.gameObject.SetActive(false);
            candidate.PrepareForPoolReuse();
            candidate.BindData(data);
            candidate.Owner = transfer.Owner;
            ItemDefinitionRuntime.ConfigureInstance(resources, definition, candidate, data);
            candidate.PoolKey = data.IDName;
            candidate.PoolMarker.CaptureBaseline();
        }
        finally
        {
            if (transfer.CandidateObject != null)
                transfer.CandidateObject.transform.SetParent(null, true);
            Destroy(staging);
        }
    }

    private void EnsureReplacementSourceCurrent(RuntimeItemReplacement transfer, bool registered)
    {
        Item original = transfer.Original;
        if (original == null || original.DestructionHandled || original.IsInPool ||
            original.RuntimeGeneration != transfer.OriginalGeneration || !ReferenceEquals(original.itemData, transfer.OriginalData) ||
            original.itemData.Guid != transfer.Guid || !string.Equals(original.itemData.IDName, transfer.OriginalDefinitionId, StringComparison.Ordinal) ||
            (registered ? !_runtimeRegistry.IsRegistrationCurrent(original, transfer.OriginalToken) : _runtimeRegistry.Contains(original)))
            throw new InvalidOperationException("替换期间源实例已结束、重新绑定或切换运行代际，不能覆盖后续实例。");
    }

    private void EnsureReplacementCandidateCurrent(RuntimeItemReplacement transfer)
    {
        EnsureSpawnRegistrationCurrent(transfer.Candidate, transfer.ReplacementData, transfer.CandidateToken);
        if (!transfer.Candidate.IsInitialized)
            throw new InvalidOperationException("替换候选体未成功建立模块运行态。");
    }

    private static void RestoreReplacementPose(RuntimeItemReplacement transfer, Item item)
    {
        item.transform.SetParent(transfer.Parent, true);
        item.transform.SetPositionAndRotation(transfer.Position, transfer.Rotation);
        item.transform.localScale = transfer.LocalScale;
        if (transfer.Parent != null) item.transform.SetSiblingIndex(transfer.SiblingIndex);
    }

    private static void AttachReplacementOwnership(RuntimeItemReplacement transfer, Item item)
    {
        if (RuntimeAiEntityUtility.IsAiEntity(item)) return;
        transfer.ChunkOwner?.AddItem(item);
        if (transfer.WasPersistedInChunk) transfer.ChunkOwner?.MapSave?.AddItemData(item.itemData);
        transfer.TransientOwner?.RegisterTransientItem(item);
    }

    private void RestoreReplacementSource(RuntimeItemReplacement transfer)
    {
        Item original = transfer.Original;
        bool registered = _runtimeRegistry.Contains(original);
        EnsureReplacementSourceCurrent(transfer, registered);
        if (!original.IsInitialized)
            transfer.OriginalState.RestoreTo(transfer.OriginalData);
        original.Owner = transfer.Owner;
        RestoreReplacementPose(transfer, original);
        ulong restoredToken = registered ? transfer.OriginalToken : 0;
        try
        {
            if (!registered)
                RegisterRuntimeItem(original, transfer.OriginalData.IDName, out restoredToken, transfer);
            if (transfer.SourceDetached) AttachReplacementOwnership(transfer, original);
            if (transfer.SourceUnloaded || !original.IsInitialized)
                original.ResumeRuntimeAfterReplacement();
            original.gameObject.SetActive(transfer.Active);
            EnsureSpawnRegistrationCurrent(original, transfer.OriginalData, restoredToken);
            if (!original.IsInitialized)
                throw new InvalidOperationException("源实例回滚后仍未成功加载模块。");
            RuntimeItemInstantiated?.Invoke(original);
            EnsureSpawnRegistrationCurrent(original, transfer.OriginalData, restoredToken);
            WorldItemWaterSystem.ScheduleSpawnCheck(original);
        }
        catch (Exception restoreError)
        {
            // 恢复失败的停止体不能占据正常运行索引，数据与外壳保留供错误定位。
            bool ownsRegistration = _runtimeRegistry.IsRegistrationCurrent(original, restoredToken);
            if (original != null && !original.DestructionHandled && !original.IsInPool &&
                ReferenceEquals(original.itemData, transfer.OriginalData) &&
                (!original.IsInitialized || !ownsRegistration) &&
                (ownsRegistration || (!_runtimeRegistry.Contains(original) && original.RuntimeGeneration == transfer.OriginalGeneration)))
            {
                Exception failure = restoreError;
                RestoreRegisteredItemIdentityForCleanup(original, restoredToken);
                try
                {
                    if (transfer.ChunkOwner != null && transfer.ChunkOwner.RunTimeItems.TryGetValue(transfer.Guid, out Item indexed) &&
                        ReferenceEquals(indexed, original))
                        transfer.ChunkOwner.RemoveItem(original);
                }
                catch (Exception error) { CaptureDespawnFailure(ref failure, error); }
                try { transfer.TransientOwner?.UnregisterTransientItem(original); }
                catch (Exception error) { CaptureDespawnFailure(ref failure, error); }
                try { original.ModuleUnload(); }
                catch (Exception error) { CaptureDespawnFailure(ref failure, error); }
                try { UnregisterRuntimeItem(original); }
                catch (Exception error) { CaptureDespawnFailure(ref failure, error); }
                try { original.gameObject.SetActive(false); }
                catch (Exception error) { CaptureDespawnFailure(ref failure, error); }
                if (!ReferenceEquals(failure, restoreError)) throw failure;
            }
            throw;
        }
    }

    private void FinishReplacedSource(RuntimeItemReplacement transfer)
    {
        Item original = transfer.Original;
        Exception cleanupError = null;
        try { original.PrepareForDespawn(saveData: false); }
        catch (Exception error) { CaptureDespawnFailure(ref cleanupError, error); }
        if (original == null || original.IsInPool || !original.DestructionHandled ||
            original.RuntimeGeneration != transfer.OriginalGeneration || !ReferenceEquals(original.itemData, transfer.OriginalData) ||
            _runtimeRegistry.Contains(original))
        {
            if (cleanupError != null) Debug.LogException(cleanupError, original);
            return;
        }
        // 食用调用返回后还会读取源数据，帧末销毁可防止同帧回池复用改变结算来源。
        Destroy(original.gameObject);
        if (cleanupError != null) Debug.LogException(cleanupError, original);
    }

    #endregion
}
