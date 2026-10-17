using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ItemMgr
{
    #region 停用候选准备

    private static readonly PerformanceDebugPoint PrepareItemDebug = new("FlatWorld.Item.PrepareCandidate");
    private static readonly PerformanceDebugPoint CommitPreparedItemDebug = new("FlatWorld.Item.CommitPreparedCandidate");
    private readonly HashSet<PreparedItemHandle> preparedItems = new();
    private Transform preparedItemRoot;

    /// <summary>只持有未登记候选；提交或取消后再次释放不会影响正式实例。</summary>
    public sealed class PreparedItemHandle : IDisposable
    {
        internal ItemMgr Manager;
        internal Item Candidate;
        internal ItemData TemporaryData;
        internal GameRes Resources;
        internal ulong DefinitionRevision;
        internal int ResourceVersion;
        internal uint CandidateGeneration;
        internal bool Committing;
        internal bool CancellationRequested;

        public bool IsValid => Manager != null && Manager.IsPreparedItemCurrent(this);
        public bool IsRegistered => Manager != null && Candidate != null && Manager._runtimeRegistry.Contains(Candidate);
        public bool IsActive => Candidate != null && Candidate.gameObject.activeInHierarchy;
        public int InstanceId => Candidate != null ? Candidate.GetInstanceID() : 0;
        public int Guid => Candidate != null ? Candidate.itemData?.Guid ?? 0 : 0;

        public void Dispose()
        {
            if (Manager != null) Manager.CancelPreparedItem(this);
        }
    }

    /// <summary>提前装配独立临时数据和模块，不注册身份、加载运行态或激活表现。</summary>
    public PreparedItemHandle PrepareItem(ItemData templateData)
    {
        if (templateData == null) throw new ArgumentNullException(nameof(templateData));
        if (string.IsNullOrWhiteSpace(templateData.IDName))
            throw new ArgumentException("准备物品缺少定义 ID。", nameof(templateData));
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(templateData.IDName, out RuntimeItemDefinition definition))
            throw new InvalidOperationException($"准备物品不存在当前定义：{templateData.IDName}。");
        ItemData temporaryData = definition.CreateItemData();
        temporaryData.IDName = templateData.IDName;
        temporaryData.Guid = 0;
        temporaryData.inHand = templateData.inHand;
        ValidatePreparedItemBackend(temporaryData);
        GameObject prefab = resources.GetPrefab(temporaryData.IDName, logError: false);
        if (prefab == null) throw new InvalidOperationException($"准备物品缺少外壳：{temporaryData.IDName}。");

        using var prepareDebug = PrepareItemDebug.Measure(1, temporaryData.IDName);
        Transform staging = GetPreparedItemRoot();
        GameObject candidateObject = null;
        Item candidate = null;
        try
        {
            // 冷对象在停用根下创建，命中对象池时复用同一套层级恢复与回收规则。
            candidateObject = _itemObjectPool.Acquire(temporaryData.IDName,
                _ => Instantiate(prefab, staging, false));
            candidate = candidateObject.GetComponent<Item>();
            if (candidate == null || candidate is Player || candidate is Map)
                throw new InvalidOperationException($"准备目标 {temporaryData.IDName} 必须是普通 Item。");
            candidateObject.SetActive(false);
            candidateObject.transform.SetParent(staging, false);
            candidate.PrepareForPoolReuse();
            candidate.IsPreparedCandidate = true;
            candidate.BindData(temporaryData);
            ItemDefinitionRuntime.ConfigureInstance(resources, definition, candidate, temporaryData);
            candidate.PoolMarker.CaptureBaseline();
            candidate.PrepareRuntimeModuleLayout();
            var handle = new PreparedItemHandle
            {
                Manager = this, Candidate = candidate, TemporaryData = temporaryData, Resources = resources,
                DefinitionRevision = resources.ItemDefinitionRevision, ResourceVersion = resources.ResourceReloadVersion,
                CandidateGeneration = candidate.RuntimeGeneration
            };
            preparedItems.Add(handle);
            return handle;
        }
        catch
        {
            if (candidate != null) ReleasePreparedCandidate(candidate, allowPool: false);
            else if (candidateObject != null) Destroy(candidateObject);
            throw;
        }
    }

    /// <summary>用最终数据和位置交接已有外壳；可保持停用供调用方整批显示。</summary>
    public Item CommitPreparedItem(PreparedItemHandle handle, ItemData latestData, Vector3 position = default,
        Quaternion rotation = default, Vector3 scale = default, GameObject parent = null, bool activate = true)
    {
        if (handle == null) throw new ArgumentNullException(nameof(handle));
        if (!ReferenceEquals(handle.Manager, this) || !IsPreparedItemCurrent(handle))
            throw new InvalidOperationException("物品准备已取消、已提交或资源版本发生变化。");
        if (latestData == null) throw new ArgumentNullException(nameof(latestData));
        if (!string.Equals(latestData.IDName, handle.TemporaryData.IDName, StringComparison.Ordinal))
            throw new InvalidOperationException("准备候选与最终物品定义不一致。");
        ValidatePreparedItemBackend(latestData);
        if (latestData.Guid != 0 && _runtimeItemReplacements.ContainsKey(latestData.Guid))
            throw new InvalidOperationException($"Item GUID {latestData.Guid} 正在进行运行时替换。");
        _runtimeRegistry.ValidateIdentityAvailable(latestData);
        if (latestData.SharedConfiguration == null)
            latestData = ItemDefinitionRuntime.RebasePersistedData(handle.Resources, latestData);

        using var commitDebug = CommitPreparedItemDebug.Measure(1, latestData.IDName);
        Item candidate = handle.Candidate;
        GameObject candidateObject = candidate.gameObject;
        ulong registrationToken = 0;
        uint? loadedGeneration = null;
        handle.Committing = true;
        try
        {
            candidate.BindPreparedRuntimeData(latestData);
            if (rotation == default) rotation = Quaternion.identity;
            if (scale == default || scale == Vector3.zero) scale = Vector3.one;
            Vector3 logicalPosition = latestData.inHand ? position : WorldLocalPresentation.ToLogical(position);
            latestData.transform ??= new ItemTransform();
            latestData.transform.position = logicalPosition;
            latestData.transform.rotation = rotation;
            latestData.transform.scale = scale;
            candidate.transform.SetParent(null, false);
            Scene targetScene = parent != null ? parent.scene : SceneManager.GetActiveScene();
            if (targetScene.IsValid() && targetScene.isLoaded && candidateObject.scene != targetScene)
                SceneManager.MoveGameObjectToScene(candidateObject, targetScene);
            candidate.transform.SetPositionAndRotation(latestData.inHand
                ? position : WorldLocalPresentation.ProjectPosition(logicalPosition), rotation);
            candidate.transform.localScale = scale;
            candidate.IsPreparedCandidate = false;
            RegisterRuntimeItem(candidate, latestData.IDName, out registrationToken);
            EnsurePreparedCommitCurrent(handle, candidate, latestData, registrationToken);
            ItemWorldPlacement.Attach(candidate, candidateObject, logicalPosition, parent);
            candidate.Load();
            loadedGeneration = candidate.RuntimeGeneration;
            EnsurePreparedCommitCurrent(handle, candidate, latestData, registrationToken);
            if (!candidate.IsInitialized)
                throw new InvalidOperationException("准备物品提交后没有成功建立模块运行态。");
            if (activate) candidateObject.SetActive(true);
            EnsurePreparedCommitCurrent(handle, candidate, latestData, registrationToken);
            RuntimeItemInstantiated?.Invoke(candidate);
            EnsurePreparedCommitCurrent(handle, candidate, latestData, registrationToken);
            if (candidate.RuntimeGeneration != loadedGeneration.Value)
                throw new InvalidOperationException("准备物品在提交回调中切换了运行代际。");
            WorldItemWaterSystem.ScheduleSpawnCheck(candidate);
            return candidate;
        }
        catch
        {
            CleanupFailedItemSpawn(candidate, candidateObject, latestData, registrationToken, loadedGeneration);
            throw;
        }
        finally { ForgetPreparedItem(handle); }
    }

    private static void ValidatePreparedItemBackend(ItemData data)
    {
        if (!data.inHand && MachineWorld.OwnsWorldItem(data))
            throw new InvalidOperationException($"设施 {data.IDName} 必须由 MachineWorld 持有，不能创建准备副本。");
        if (AiRuntimeBackendService.UsesEntities(data.IDName))
            throw new InvalidOperationException($"生物 {data.IDName} 必须通过 ECS 入口创建。");
    }

    private bool HasPreparedResourceVersion(PreparedItemHandle handle) => handle.Resources != null &&
        ReferenceEquals(GameRes.ExistingInstance, handle.Resources) &&
        handle.Resources.ItemDefinitionRevision == handle.DefinitionRevision &&
        handle.Resources.ResourceReloadVersion == handle.ResourceVersion;

    private bool IsPreparedItemCurrent(PreparedItemHandle handle)
    {
        Item item = handle.Candidate;
        return !handle.Committing && !handle.CancellationRequested && preparedItems.Contains(handle) &&
            HasPreparedResourceVersion(handle) && item != null && item.IsPreparedCandidate &&
            !item.gameObject.activeSelf && !item.IsInitialized && !item.DestructionHandled && !item.IsInPool &&
            !item.IsRuntimeTransitioning && item.RuntimeGeneration == handle.CandidateGeneration &&
            ReferenceEquals(item.itemData, handle.TemporaryData) && !_runtimeRegistry.Contains(item);
    }

    private void EnsurePreparedCommitCurrent(PreparedItemHandle handle, Item item, ItemData data, ulong token)
    {
        if (handle.CancellationRequested || !HasPreparedResourceVersion(handle))
            throw new InvalidOperationException("准备物品在提交期间已取消或资源版本发生变化。");
        EnsureSpawnRegistrationCurrent(item, data, token);
    }

    private Transform GetPreparedItemRoot()
    {
        if (preparedItemRoot != null) return preparedItemRoot;
        GameObject root = new("ItemPreparationStaging");
        root.SetActive(false);
        preparedItemRoot = root.transform;
        return preparedItemRoot;
    }

    private void CancelPreparedItem(PreparedItemHandle handle, bool allowPool = true)
    {
        if (!ReferenceEquals(handle.Manager, this) || !preparedItems.Contains(handle)) return;
        if (handle.Committing)
        {
            handle.CancellationRequested = true;
            return;
        }
        Item candidate = handle.Candidate;
        bool mayRelease = candidate != null && candidate.IsPreparedCandidate && !candidate.IsInPool &&
            candidate.RuntimeGeneration == handle.CandidateGeneration && ReferenceEquals(candidate.itemData, handle.TemporaryData) &&
            !_runtimeRegistry.Contains(candidate);
        bool canPool = allowPool && HasPreparedResourceVersion(handle);
        ForgetPreparedItem(handle);
        if (mayRelease) ReleasePreparedCandidate(candidate, canPool);
    }

    private void ReleasePreparedCandidate(Item candidate, bool allowPool)
    {
        if (candidate == null) return;
        bool returned = false;
        try
        {
            candidate.PrepareForDespawn(saveData: false);
            if (allowPool) returned = TryReturnItemToPool(candidate);
            candidate.IsPreparedCandidate = false;
        }
        catch (Exception error) { Debug.LogException(error, candidate); }
        finally
        {
            if (!returned && candidate != null)
            {
                candidate.PoolingDisabled = true;
                candidate.IsPreparedCandidate = false;
                Destroy(candidate.gameObject);
            }
        }
    }

    private void ForgetPreparedItem(PreparedItemHandle handle)
    {
        preparedItems.Remove(handle);
        handle.Manager = null;
        handle.Candidate = null;
        handle.TemporaryData = null;
        handle.Resources = null;
        handle.Committing = false;
    }

    private void ClearPreparedItems()
    {
        foreach (PreparedItemHandle handle in new List<PreparedItemHandle>(preparedItems))
            CancelPreparedItem(handle, allowPool: false);
        if (preparedItemRoot != null) Destroy(preparedItemRoot.gameObject);
        preparedItemRoot = null;
    }

    #endregion
}
