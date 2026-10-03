using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// ChunkView 的自然生态与临时掉落物表现适配器。
/// 后台只返回 NaturalItemPlacement；资源生成直接装配 Entity 并绑定区块 BRG。
/// 未纳入本轮迁移的传送门、蜂巢等独立玩法仍走原入口，资源不会退回完整 Item。
/// 应用退出不保存生态差量；已有管理器仍存活时正常注销物品，依赖引用仅在绑定和登记时获取。
/// 初次分帧绑定每步最多尝试创建一个实体，宿主全部处理完后才处理伴生物。
/// </summary>
public sealed class ChunkNaturalItemRenderer : MonoBehaviour, IIncrementalChunkViewRenderer
{
    #region 字段

    private const float AutoSaveFrameBudgetSeconds = 0.0025f;
    private const float LooseItemOffsetRadius = 0.15f;
    private static readonly ProfilerMarker NaturalItemSpawnMarker =
        new("FlatWorld.ChunkStreaming.SpawnNaturalItem");
    private static readonly ProfilerMarker NaturalItemCaptureMarker =
        new("FlatWorld.ChunkStreaming.CaptureNaturalItems");
    private static readonly ProfilerMarker NaturalItemDespawnMarker =
        new("FlatWorld.ChunkStreaming.DespawnNaturalItems");

    private readonly Dictionary<int, Item> spawnedItems = new();
    private readonly HashSet<Item> transientItems = new();
    private readonly List<Item> unbindItems = new();
    private readonly HashSet<Item> unbindItemSet = new();
    private readonly HashSet<int> generatedPortalGuids = new();
    private readonly List<NaturalItemPlacement> deferredCompanionPlacements = new();
    private readonly Dictionary<int, StaticNaturalRuntime> staticEntities = new();
    private readonly List<StaticNaturalRuntime> staticCaptureBuffer = new();
    private ChunkRuntime boundChunk;
    private ChunkTilemapRenderer terrainOwner;
    private EnvironmentLayers environmentLayers;
    // 绑定时记录依赖，销毁阶段不再通过单例搜索场景。
    private ItemMgr itemManager;
    private ChunkMgr chunkManager;
    private bool applicationQuitting;
    private bool unbinding;
    private bool initialBindingInProgress;
    private float nextRenewalCheck; // 春季低频分批补位。
    private int renewalCursor; // 每次最多检查四个自然生成点。
    private float nextCompanionReadinessCheck; // 小树长大后低频补生成伴生物。
    private int companionReadinessCursor;

    private sealed class StaticNaturalRuntime
    {
        public NaturalEntityData Data;
        public NaturalEntityHandle Handle;
    }

    public int SpawnedItemCount => spawnedItems.Count;
    public int EcsEntityCount => staticEntities.Count;
    public int EcsOnlyEntityCount => staticEntities.Count;

    /// <summary>按稳定 GUID 查询当前区块已经实例化的自然物。</summary>
    public bool TryGetSpawnedItem(int guid, out Item item)
    {
        return spawnedItems.TryGetValue(guid, out item) && item != null;
    }

    /// <summary>登记区块临时掉落物，使解绑时回收它而不写入生态存档。</summary>
    public void RegisterTransientItem(Item item)
    {
        if (applicationQuitting || unbinding || item == null || item.DestructionHandled ||
            !transientItems.Add(item))
            return;

        if (itemManager == null)
            itemManager = ItemMgr.Instance;
        item.OnItemDestroy -= HandleTransientItemDestroy;
        item.OnItemDestroy += HandleTransientItemDestroy;
    }

    /// <summary>临时物品跨区块移动前解除当前 ChunkView 的归属与销毁监听。</summary>
    public void UnregisterTransientItem(Item item)
    {
        if (item == null)
            return;

        item.OnItemDestroy -= HandleTransientItemDestroy;
        transientItems.Remove(item);
    }

    /// <summary>临时掉落物被拾取或销毁后解除登记。</summary>
    private void HandleTransientItemDestroy(Item item)
    {
        UnregisterTransientItem(item);
    }

    #endregion

    #region 绑定生命周期

    /// <summary>同步入口沿用同一套绑定步骤，供即时重绑场景调用。</summary>
    public void Bind(ChunkRuntime chunk)
    {
        IEnumerator steps = BindIncremental(chunk);
        try
        {
            while (steps.MoveNext()) { }
        }
        finally
        {
            (steps as IDisposable)?.Dispose();
        }
    }

    /// <summary>先创建宿主再创建伴生物，每步最多实例化一个自然实体。</summary>
    public IEnumerator BindIncremental(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new InvalidOperationException("Cannot bind natural items before terrain is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            yield break;

        Unbind();
        itemManager = ItemMgr.GetInstance();
        chunkManager = ChunkMgr.ExistingInstance;
        boundChunk = chunk;
        terrainOwner = GetComponentInParent<ChunkView>()?.GetComponentInChildren<ChunkTilemapRenderer>(true);
        if (terrainOwner != null)
            terrainOwner.BatchPresentationRebuilt += RefreshStaticVisuals;
        if (chunkManager != null)
            chunkManager.NaturalItemRemoved += HandleStaticRemoved;
        initialBindingInProgress = true;

        try
        {
            environmentLayers = BuildEnvironmentLayers(chunk.Terrain);
            IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
            if (placements == null || placements.Count == 0)
                yield break;
            if (itemManager == null || chunkManager == null)
                throw new InvalidOperationException("绑定自然物需要有效的 ItemMgr 和 ChunkMgr。");

            // 宿主必须先完成生成，伴生物才能按宿主状态决定是否出现。
            int dataEntityBatch = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                NaturalItemPlacement placement = placements[i];
                if (!placement.IsCompanion && TryBindStaticEntity(placement))
                {
                    if (++dataEntityBatch >= 16)
                    {
                        dataEntityBatch = 0;
                        yield return null;
                    }
                    continue;
                }
                if (!placement.IsCompanion && SpawnInitialPlacement(placement))
                    yield return null;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                NaturalItemPlacement placement = placements[i];
                if (!placement.IsCompanion)
                    continue;

                if (!CanSpawnCompanionForHost(placement))
                {
                    if (!chunkManager.IsNaturalItemRemoved(chunk.Address, placement.Guid))
                        deferredCompanionPlacements.Add(placement);
                    continue;
                }

                if (SpawnInitialPlacement(placement))
                    yield return null;
            }
        }
        finally
        {
            initialBindingInProgress = false;
        }
    }

    /// <summary>传送门创建后立即同步碰撞体，分帧等待期间也能正常交互。</summary>
    private bool SpawnInitialPlacement(NaturalItemPlacement placement)
    {
        int portalCount = generatedPortalGuids.Count;
        bool spawned;
        using (NaturalItemSpawnMarker.Auto())
            spawned = SpawnPlacement(placement);
        if (generatedPortalGuids.Count > portalCount)
            Physics2D.SyncTransforms();
        return spawned;
    }

    /// <summary>正常解绑时保存并注销物品；管理器已销毁时只解除登记，由场景销毁子对象。</summary>
    public void Unbind()
    {
        if (unbinding || (boundChunk == null && spawnedItems.Count == 0 && transientItems.Count == 0))
            return;

        unbinding = true;
        try
        {
            if (chunkManager != null)
                chunkManager.NaturalItemRemoved -= HandleStaticRemoved;
            if (terrainOwner != null)
                terrainOwner.BatchPresentationRebuilt -= RefreshStaticVisuals;
            using (NaturalItemCaptureMarker.Auto())
                CaptureStateForUnbind();
            unbindItems.Clear();
            unbindItemSet.Clear();
            foreach (Item item in spawnedItems.Values)
            {
                unbindItems.Add(item);
                unbindItemSet.Add(item);
            }
            foreach (Item transientItem in transientItems)
            {
                if (transientItem != null && unbindItemSet.Add(transientItem))
                    unbindItems.Add(transientItem);
            }

            using (NaturalItemDespawnMarker.Auto())
            {
                for (int i = 0; i < unbindItems.Count; i++)
                {
                    Item item = unbindItems[i];
                    if (item == null)
                        continue;
                    item.OnItemDestroy -= HandleNaturalItemDestroy;
                    item.OnItemDestroy -= HandleTransientItemDestroy;
                    if (itemManager != null && !item.DestructionHandled)
                        itemManager.DespawnItem(item, saveData: false, detachFromChunk: false);
                }
            }
        }
        finally
        {
            unbindItems.Clear();
            unbindItemSet.Clear();
            spawnedItems.Clear();
            foreach (StaticNaturalRuntime runtime in staticEntities.Values)
                NaturalEntityEcsService.Remove(runtime.Handle);
            staticEntities.Clear();
            staticCaptureBuffer.Clear();
            transientItems.Clear();
            generatedPortalGuids.Clear();
            deferredCompanionPlacements.Clear();
            companionReadinessCursor = 0;
            nextCompanionReadinessCheck = 0f;
            initialBindingInProgress = false;
            environmentLayers = null;
            boundChunk = null;
            terrainOwner = null;
            itemManager = null;
            chunkManager = null;
            unbinding = false;
        }
    }

    /// <summary>停止播放和关闭程序都在对象销毁前关闭生态保存，避免把退出误记为采集。</summary>
    private void OnApplicationQuit() => applicationQuitting = true;

    /// <summary>保存当前自然物状态，不改变实例生命周期。</summary>
    public void CaptureState()
    {
        if (applicationQuitting || boundChunk == null || chunkManager == null ||
            chunkManager.IsWorldRuntimeShuttingDown || !GameNetwork.HasStateAuthority)
        {
            return;
        }

        CaptureSpawnedItemStates();
        CaptureStaticEntityStates();
    }

    /// <summary>普通 Item 仍需复制快照，因为解绑后的对象池实例会继续复用原 ItemData。</summary>
    private void CaptureSpawnedItemStates()
    {
        RuntimeWorldAddress address = boundChunk.Address;
        foreach (KeyValuePair<int, Item> pair in spawnedItems)
        {
            Item item = pair.Value;
            if (item == null || item.itemData == null || item.DestructionHandled)
                continue;
            // 天然传送门不保存运行态快照；真正被摧毁时仍由 OnItemDestroy 写入删除差量。
            if (generatedPortalGuids.Contains(pair.Key))
                continue;

            try
            {
                item.Save();
                ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
                chunkManager.CaptureNaturalItemState(address, snapshot);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ChunkNaturalItemRenderer] 保存自然物失败：{item.name}，{exception}",
                    item);
            }
        }
    }

    /// <summary>区块解绑时直接移交 ECS 内部快照并释放实体，避免批量 DeepClone 造成物理帧尖峰。</summary>
    private void CaptureStateForUnbind()
    {
        bool canCapture = !applicationQuitting && boundChunk != null && chunkManager != null &&
                          !chunkManager.IsWorldRuntimeShuttingDown && GameNetwork.HasStateAuthority;
        if (canCapture)
            CaptureSpawnedItemStates();

        staticCaptureBuffer.Clear();
        staticCaptureBuffer.AddRange(staticEntities.Values);
        RuntimeWorldAddress address = boundChunk != null ? boundChunk.Address : default;
        for (int i = 0; i < staticCaptureBuffer.Count; i++)
        {
            NaturalEntityHandle handle = staticCaptureBuffer[i].Handle;
            if (canCapture && NaturalEntityEcsService.TryTakeSnapshotAndRemove(handle, out ItemData snapshot))
                chunkManager.CaptureNaturalItemState(address, snapshot);
            else
                NaturalEntityEcsService.Remove(handle);
        }
        staticCaptureBuffer.Clear();
        staticEntities.Clear();
    }

    /// <summary>自动保存专用的自然物分帧快照，避免一次克隆全部表现物。</summary>
    public IEnumerator CaptureStateCoroutine()
    {
        if (applicationQuitting || boundChunk == null || chunkManager == null ||
            chunkManager.IsWorldRuntimeShuttingDown || !GameNetwork.HasStateAuthority)
        {
            yield break;
        }

        RuntimeWorldAddress address = boundChunk.Address;
        ChunkRuntime capturedChunk = boundChunk;
        List<KeyValuePair<int, Item>> items = new List<KeyValuePair<int, Item>>(spawnedItems);
        float frameStart = Time.realtimeSinceStartup;
        for (int i = 0; i < items.Count; i++)
        {
            // 分帧期间发生退出或重新绑定时，旧快照不能继续写回世界。
            if (applicationQuitting || !ReferenceEquals(boundChunk, capturedChunk) ||
                chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown)
                yield break;

            KeyValuePair<int, Item> pair = items[i];
            Item item = pair.Value;
            if (item != null && item.itemData != null && !item.DestructionHandled &&
                !generatedPortalGuids.Contains(pair.Key))
            {
                try
                {
                    item.Save();
                    ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
                    chunkManager.CaptureNaturalItemState(address, snapshot);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[ChunkNaturalItemRenderer] 分帧保存自然物失败：{item.name}，{exception}",
                        item);
                }
            }

            if (Time.realtimeSinceStartup - frameStart >= AutoSaveFrameBudgetSeconds)
            {
                frameStart = Time.realtimeSinceStartup;
                yield return null;
            }
        }

        List<StaticNaturalRuntime> staticRuntimes = new(staticEntities.Values);
        for (int i = 0; i < staticRuntimes.Count; i++)
        {
            if (applicationQuitting || !ReferenceEquals(boundChunk, capturedChunk) ||
                chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown)
                yield break;
            StaticNaturalRuntime runtime = staticRuntimes[i];
            NaturalEntityEcsService.PrepareForCapture(runtime.Handle);
            if (NaturalEntityEcsService.TryCapture(runtime.Handle, out ItemData snapshot))
                chunkManager.CaptureNaturalItemState(address, snapshot);
            if (Time.realtimeSinceStartup - frameStart >= AutoSaveFrameBudgetSeconds)
            {
                frameStart = Time.realtimeSinceStartup;
                yield return null;
            }
        }
    }

    #endregion

    #region 静态自然物 ECS

    /// <summary>声明为资源实体的内容始终由 Entity 运行，不因距离或联机状态切回 Item。</summary>
    private bool TryBindStaticEntity(NaturalItemPlacement placement)
    {
        if (placement.IsDimensionPortal || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) ||
            !definition.UsesResourceEntities) return false;
        if (staticEntities.ContainsKey(placement.Guid)) return true;
        if (terrainOwner == null || chunkManager == null)
            throw new InvalidOperationException("资源实体需要有效的区块表现与世界上下文。");
        if (!CanSpawnCompanionForHost(placement)) return true;

        RuntimeWorldAddress address = boundChunk.Address;
        bool renewing = chunkManager.IsNaturalItemRemoved(address, placement.Guid);
        if (renewing && (!chunkManager.IsNaturalRenewalDue(address, placement.Guid) || !CanRenewAt(placement)))
            return true;
        chunkManager.TryGetNaturalItemOverride(address, placement.Guid, out ItemData changedData);
        boundChunk.Terrain.TryGetEnvironmentValue("temperature.celsius", placement.LocalX, placement.LocalY, out float baseline);
        boundChunk.Terrain.TryGetEnvironmentValue("precipitation", placement.LocalX, placement.LocalY, out float precipitation);
        Vector3 position = new(address.ChunkOrigin.X + placement.LocalX + 0.5f + placement.OffsetX,
            address.ChunkOrigin.Y + placement.LocalY + 0.5f + placement.OffsetY);
        if (!NaturalEntityEcsService.TryRegister(definition, placement.Guid, position, baseline, precipitation,
                address.DimensionId, changedData, out NaturalEntityHandle handle))
            throw new InvalidOperationException($"资源实体 {placement.ItemId} 未能建立，禁止退回 GameObject。");
        var runtime = new StaticNaturalRuntime { Data = new NaturalEntityData(placement), Handle = handle };
        try
        {
            staticEntities.Add(placement.Guid, runtime);
            NaturalEntityEcsService.BindRemovalHandler(handle, HandleEntityRemoved);
            NaturalEntityEcsService.BindPresentation(handle, terrainOwner);
            if (renewing) chunkManager.CompleteNaturalRenewal(address, placement.Guid);
        }
        catch
        {
            staticEntities.Remove(placement.Guid);
            NaturalEntityEcsService.Remove(handle);
            throw;
        }
        return true;
    }

    /// <summary>资源刷新只重编配置和重提实例；生命周期与玩法状态仍属于原 Entity。</summary>
    private void RefreshStaticVisuals()
    {
        foreach (StaticNaturalRuntime runtime in staticEntities.Values)
        {
            if (GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(runtime.Data.ItemId, out RuntimeItemDefinition definition)) continue;
            if (!NaturalEntityEcsService.TryRefreshDefinition(runtime.Handle, definition))
                Debug.LogError($"资源实体 {runtime.Data.ItemId} 配置无法刷新，保留原状态，不创建 Item 外壳。");
            NaturalEntityEcsService.BindPresentation(runtime.Handle, terrainOwner);
        }
    }

    private void HandleStaticRemoved(RuntimeWorldAddress address, int guid)
    {
        if (boundChunk == null || boundChunk.Address != address ||
            !staticEntities.TryGetValue(guid, out StaticNaturalRuntime runtime)) return;
        staticEntities.Remove(guid);
        NaturalEntityEcsService.Remove(runtime.Handle);
    }

    /// <summary>只有真实死亡或采集提交删除；解绑只保存并释放，不触发生态续生。</summary>
    private void HandleEntityRemoved(NaturalEntityHandle handle)
    {
        if (applicationQuitting || boundChunk == null || chunkManager == null ||
            chunkManager.IsWorldRuntimeShuttingDown || !GameNetwork.HasStateAuthority) return;
        if (!NaturalEntityEcsService.TryCapture(handle, out ItemData snapshot) ||
            !staticEntities.TryGetValue(snapshot.Guid, out StaticNaturalRuntime runtime) || runtime.Handle != handle) return;
        bool renews = NaturalEntityEcsService.TryGetRenewalYear(handle, out int year);
        chunkManager.MarkNaturalItemRemoved(boundChunk.Address, snapshot.Guid);
        staticEntities.Remove(snapshot.Guid);
        if (renews) chunkManager.ScheduleNaturalRenewal(boundChunk.Address, snapshot.Guid, year);
    }

    private void CaptureStaticEntityStates()
    {
        if (boundChunk == null || chunkManager == null) return;
        // 死亡结算可能移除字典条目，快照遍历避免事件重入破坏枚举。
        staticCaptureBuffer.Clear();
        staticCaptureBuffer.AddRange(staticEntities.Values);
        for (int i = 0; i < staticCaptureBuffer.Count; i++)
        {
            StaticNaturalRuntime runtime = staticCaptureBuffer[i];
            NaturalEntityEcsService.PrepareForCapture(runtime.Handle);
            if (NaturalEntityEcsService.TryCapture(runtime.Handle, out ItemData snapshot))
                chunkManager.CaptureNaturalItemState(boundChunk.Address, snapshot);
        }
        staticCaptureBuffer.Clear();
    }

    #endregion

    #region 物品实例化

    /// <summary>应用删除/状态覆盖后实例化一个自然物；返回本次是否尝试创建实体。</summary>
    private bool SpawnPlacement(NaturalItemPlacement placement)
    {
        if (boundChunk == null || itemManager == null || placement.Guid == 0 ||
            string.IsNullOrWhiteSpace(placement.ItemId))
        {
            return false;
        }
        if (!CanSpawnCompanionForHost(placement))
            return false;
        if (TryBindStaticEntity(placement)) return true;

        RuntimeWorldAddress address = boundChunk.Address;
        string runtimeItemId = placement.ItemId;
        // 地表植被保留纯生成点和删除差量，但由专用 Tilemap 绘制，不创建常驻 Item。
        if (GameRes.ExistingInstance.TryGetItemDefinition(runtimeItemId, out RuntimeItemDefinition definition) &&
            definition.IsGroundCover)
            return false;
        bool renewing = chunkManager.IsNaturalItemRemoved(address, placement.Guid);
        if (renewing && (!chunkManager.IsNaturalRenewalDue(address, placement.Guid) || !CanRenewAt(placement))) return false;

        Vector3 position = new Vector3(
            address.ChunkOrigin.X + placement.LocalX + 0.5f + placement.OffsetX,
            address.ChunkOrigin.Y + placement.LocalY + 0.5f + placement.OffsetY,
            0f);
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;
        ItemData changedData = null;
        if (chunkManager != null)
            chunkManager.TryGetNaturalItemOverride(address, placement.Guid, out changedData);

        Item item = null;
        try
        {
            // 设施基线只生成一次机器数据，视觉卸载不得销毁权威设施或重置库存。
            if (!placement.IsDimensionPortal && MachineCatalog.Get(changedData?.IDName ?? runtimeItemId) != null)
            {
                ItemData machineData = changedData ?? definition.CreateItemData();
                machineData.Guid = placement.Guid;
                MachineEntity machine = MachineWorld.SpawnGenerated(machineData, changedData?.transform?.position ?? position);
                if (machine != null) chunkManager.MarkNaturalItemRemoved(address, placement.Guid);
                return true;
            }
            // 可直接拾取的散落生成点统一使用掉落物尺寸；能轻量化的单机实例再交给统一掉落服务。
            ItemData looseData = changedData ?? definition?.CreateItemData();
            bool loosePickupPresentation = false;
            if (!placement.IsDimensionPortal && definition != null && !definition.IsActor && looseData?.Stack?.CanBePickedUp == true)
            {
                bool installed = Mod_Building.TryReadBuildingData(looseData, out _, out Mod_Building.Building_Data building) &&
                    building.Role == BuildingRole.PlacedBuilding;
                if (!installed)
                {
                    loosePickupPresentation = true;
                    if (changedData?.transform != null)
                    {
                        position = changedData.transform.position;
                        rotation = changedData.transform.rotation;
                    }

                    ApplyLooseItemPresentation(placement, changedData == null, ref position, ref rotation);
                    scale = DroppedItemService.ResolveDefaultWorldDropScale(looseData);

                    bool canUseDropService = DroppedItemService.UsesLightweightDrops &&
                        definition.ShellPrefab != null &&
                        definition.ShellPrefab.GetComponentInChildren<Mod_TileEffectReceiver>(true) == null;
                    if (canUseDropService)
                    {
                        // 已经用稳定 GUID 生成了偏移，显式传同一终点避免服务再叠一层随机位移。
                        DroppedItemHandle handle = DroppedItemService.Spawn(looseData, position, position,
                            rotation: rotation.eulerAngles.z, randomizeRotation: false);
                        try { chunkManager.MarkNaturalItemRemoved(address, placement.Guid); }
                        catch { DroppedItemService.Remove(handle); throw; }
                        return true;
                    }
                }
            }

            if (changedData != null && !string.IsNullOrWhiteSpace(changedData.IDName))
            {
                if (changedData.transform != null)
                {
                    position = changedData.transform.position;
                    rotation = changedData.transform.rotation;
                    if (!loosePickupPresentation)
                        scale = changedData.transform.scale;
                }
                item = itemManager.InstantiateItem(
                    changedData, position, rotation, scale, gameObject);
            }
            else
            {
                item = itemManager.InstantiateItemDeterministic(
                    runtimeItemId,
                    placement.Guid,
                    position,
                    rotation,
                    scale,
                    gameObject);
            }

            if (item == null)
                return true;

            item.Load();
            if (placement.IsDimensionPortal)
            {
                Mod_DimensionPortal portal = item.GetComponentInChildren<Mod_DimensionPortal>(true);
                if (portal == null)
                    throw new InvalidOperationException(
                        $"生成传送门物品缺少 Mod_DimensionPortal：{runtimeItemId}");
                // 显式绑定拥有者，切换维度时不再依赖父层级查找的时序。
                portal.ConfigureGenerated(placement.TargetDimensionId, item);
                if (item.itemData.Stack != null)
                    item.itemData.Stack.CanBePickedUp = false;
                generatedPortalGuids.Add(placement.Guid);
            }
            INaturalResourceInitializer[] initializers =
                item.GetComponentsInChildren<INaturalResourceInitializer>(true);
            foreach (INaturalResourceInitializer initializer in initializers)
                initializer.InitializeNaturalResource(unchecked((uint)placement.Guid));
            item.Initialize_Env(environmentLayers,
                new Vector2Int(placement.LocalX, placement.LocalY));
            item.OnItemDestroy += HandleNaturalItemDestroy;
            spawnedItems[placement.Guid] = item;
            if (renewing) chunkManager.CompleteNaturalRenewal(address, placement.Guid);
            return true;
        }
        catch (Exception exception)
        {
            if (item != null && itemManager != null && !item.DestructionHandled)
                itemManager.DespawnItem(item, saveData: false, detachFromChunk: false);
            Debug.LogWarning(
                $"[ChunkNaturalItemRenderer] 自然物实例化失败：{placement.ItemId}，规则={placement.RuleId}，{exception.Message}",
                this);
            return true;
        }
    }

    /// <summary>自然散落物用稳定 GUID 生成轻微偏移和旋转，避免规则格中心整齐排布。</summary>
    private static void ApplyLooseItemPresentation(NaturalItemPlacement placement, bool applyRandomPose,
        ref Vector3 position, ref Quaternion rotation)
    {
        if (!applyRandomPose)
            return;

        unchecked
        {
            uint hash = (uint)placement.Guid * 747796405u + 2891336453u;
            float angle = (hash & 0xffffu) / 65535f * Mathf.PI * 2f;
            hash = hash * 277803737u + 1013904223u;
            float radius = ((hash >> 16) & 0xffffu) / 65535f * LooseItemOffsetRadius;
            position.x += Mathf.Cos(angle) * radius;
            position.y += Mathf.Sin(angle) * radius;

            hash = hash * 277803737u + 1013904223u;
            float rotation01 = (hash & 0xffffu) / 65535f;
            rotation = Quaternion.Euler(0f, 0f, rotation01 * 360f);
        }
    }

    /// <summary>自然物被玩家采集或其它系统销毁时写入删除列表。</summary>
    private void HandleNaturalItemDestroy(Item item)
    {
        if (applicationQuitting || unbinding || item == null || item.itemData == null)
            return;

        int guid = item.itemData.Guid;
        spawnedItems.Remove(guid);
        if (boundChunk != null && chunkManager != null && !chunkManager.IsWorldRuntimeShuttingDown)
        {
            chunkManager.MarkNaturalItemRemoved(boundChunk.Address, guid);
            foreach (Module module in item.itemMods.Mods.Values)
                if (module is INaturalRenewalPolicy policy && policy.TryGetRenewalYear(out int year))
                    chunkManager.ScheduleNaturalRenewal(boundChunk.Address, guid, year);
        }
    }

    /// <summary>低频检查等待宿主长大的伴生物；达到宿主门槛后只尝试生成一次。</summary>
    private void Update()
    {
        if (initialBindingInProgress)
            return;
        ProcessDeferredCompanionSpawns();
        ProcessNaturalRenewal();
    }

    private void ProcessDeferredCompanionSpawns()
    {
        if (unbinding || boundChunk == null || chunkManager == null ||
            deferredCompanionPlacements.Count == 0 || Time.unscaledTime < nextCompanionReadinessCheck)
        {
            return;
        }

        nextCompanionReadinessCheck = Time.unscaledTime + 1f;
        int checks = Mathf.Min(4, deferredCompanionPlacements.Count);
        for (int i = 0; i < checks && deferredCompanionPlacements.Count > 0; i++)
        {
            if (companionReadinessCursor >= deferredCompanionPlacements.Count)
                companionReadinessCursor = 0;

            NaturalItemPlacement placement = deferredCompanionPlacements[companionReadinessCursor];
            if (spawnedItems.ContainsKey(placement.Guid) ||
                chunkManager.IsNaturalItemRemoved(boundChunk.Address, placement.Guid))
            {
                deferredCompanionPlacements.RemoveAt(companionReadinessCursor);
                continue;
            }

            if (!CanSpawnCompanionForHost(placement))
            {
                companionReadinessCursor++;
                continue;
            }

            deferredCompanionPlacements.RemoveAt(companionReadinessCursor);
            SpawnPlacement(placement);
        }
    }

    /// <summary>宿主存在且所有宿主模块都同意时，伴生物才允许实例化。</summary>
    private bool CanSpawnCompanionForHost(NaturalItemPlacement placement)
    {
        if (!placement.IsCompanion)
            return true;
        if (spawnedItems.TryGetValue(placement.HostGuid, out Item host) && host != null &&
            !host.DestructionHandled && host.itemMods != null)
        {
            foreach (Module module in host.itemMods.Mods.Values)
            {
                if (module is INaturalCompanionHostCondition condition &&
                    !condition.CanHostNaturalCompanion(placement.ItemId))
                    return false;
            }
            return true;
        }
        return staticEntities.TryGetValue(placement.HostGuid, out StaticNaturalRuntime runtime) &&
               NaturalEntityEcsService.TryCanHostCompanion(runtime.Handle, out bool canHost) &&
               canHost;
    }

    /// <summary>春季逐步补回已被移除的自然植物，不扫描或加载窗口以外的区块。</summary>
    private void ProcessNaturalRenewal()
    {
        if (unbinding || boundChunk?.Ecology?.Placements == null || Time.unscaledTime < nextRenewalCheck ||
            !GameNetwork.HasStateAuthority || !DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season) ||
            season.Season != WorldSeason.Spring) return;
        nextRenewalCheck = Time.unscaledTime + 1f;
        IReadOnlyList<NaturalItemPlacement> placements = boundChunk.Ecology.Placements;
        for (int i = 0; i < 4 && placements.Count > 0; i++)
        {
            NaturalItemPlacement placement = placements[renewalCursor++ % placements.Count];
            if (!spawnedItems.ContainsKey(placement.Guid) &&
                !staticEntities.ContainsKey(placement.Guid) &&
                chunkManager.IsNaturalRenewalDue(boundChunk.Address, placement.Guid))
            {
                if (!TryBindStaticEntity(placement))
                    SpawnPlacement(placement);
                break;
            }
        }
    }
    /// <summary>恢复点必须仍为空地，禁止覆盖玩家建筑、耕地或平台。</summary>
    private bool CanRenewAt(NaturalItemPlacement placement)
    {
        var cell = boundChunk.Terrain.GetCell(placement.LocalX, placement.LocalY);
        Vector2Int world = new(boundChunk.Address.ChunkOrigin.X + placement.LocalX,
            boundChunk.Address.ChunkOrigin.Y + placement.LocalY);
        return boundChunk.Terrain.GetLiquidDepth(placement.LocalX, placement.LocalY) <= 0f &&
            cell.BlockingTileId == 0 && cell.BackTileId == 0 && !FarmlandSystem.IsFarmland(cell) &&
            TerrainSupportLayer.GetTileId(boundChunk.Terrain, placement.LocalX, placement.LocalY) == 0 &&
            !BuildingOccupancyRegistry.IsOccupied(world);
    }

    #endregion

    #region 环境适配

    /// <summary>把纯地形环境数组适配成现有 Item 模块使用的 EnvironmentLayers。</summary>
    private static EnvironmentLayers BuildEnvironmentLayers(ChunkTerrainData terrain)
    {
        var layers = new EnvironmentLayers();
        layers.EnsureSize(terrain.Width, terrain.Height);
        CopyLayer(terrain, "temperature", layers.Temperature);
        CopyLayer(terrain, "temperature.celsius", layers.TemperatureCelsius);
        CopyLayer(terrain, "precipitation", layers.Precipitation);
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            layers.WindX[x, y] = 1f;
            layers.WindY[x, y] = 0f;
            layers.Light[x, y] = 1f;
        }
        return layers;
    }

    /// <summary>复制一个一维纯数据层到 EnvironmentLayers 的二维数组。</summary>
    private static void CopyLayer(ChunkTerrainData terrain, string layerId, float[,] target)
    {
        if (!terrain.TryCopyEnvironmentLayer(layerId, out float[] values))
            return;

        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            target[x, y] = values[y * terrain.Width + x];
    }

    #endregion
}
