using System.Collections.Generic;
using System;
using FlatWorld.NaturalEntities;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Tilemaps;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 区块农业表现：未完成锄地只画临时耕地覆盖层，铲挖则按权威进度渐显目标地块。
/// 作物在自动保存和区块解绑时抓取快照，收获时清除快照；不复用自然生态或临时掉落登记。
/// </summary>
public sealed class ChunkAgricultureRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private Material progressMaterial; // 支持 SpriteRenderer 透明度的耕地渐显材质
    [SerializeField, Min(0.01f)] private float tillingFadeDuration = 0.18f;
    [SerializeField, Range(0f, 1f)] private float minimumProgressAlpha = 0.12f;
    [SerializeField, Range(0f, 1f)] private float maximumProgressAlpha = 0.9f;

    private sealed class ProgressOverlay
    {
        public SpriteRenderer Renderer;
        public float TargetAlpha;
    }

    private readonly Dictionary<Vector2Int, ProgressOverlay> tillingOverlays = new();
    private readonly Dictionary<Vector2Int, ProgressOverlay> groundHarvestOverlays = new();
    private readonly List<Vector2Int> tillingCellBuffer = new();
    private readonly Dictionary<Vector2Int, Item> crops = new();
    private readonly Dictionary<Vector2Int, NaturalEntityHandle> entityCrops = new();
    private readonly Dictionary<int, Vector2Int> entityCropCells = new();
    private readonly Dictionary<int, int> entityCropGuids = new();
    private static readonly Dictionary<int, (ChunkAgricultureRenderer Owner, Vector2Int Local)> cropOwners = new();
    private readonly List<KeyValuePair<Vector2Int, NaturalEntityHandle>> entityCropCaptureBuffer = new();
    private ChunkTilemapRenderer terrainOwner;
    private static readonly ProfilerMarker CropCaptureMarker =
        new("FlatWorld.ChunkStreaming.CaptureCrops");
    private static readonly ProfilerMarker CropDespawnMarker =
        new("FlatWorld.ChunkStreaming.DespawnCrops");
    private static readonly ProfilerMarker OverlayClearMarker =
        new("FlatWorld.ChunkStreaming.ClearTillingOverlays");
    private static readonly ProfilerMarker TillingRefreshMarker =
        new("FlatWorld.Agriculture.RefreshTillingOverlays");
    private ChunkRuntime chunk;
    private bool unbinding;
    private bool applicationQuitting;
    private bool bindingInitialState;
    private bool hasActiveFade;
    private float nextTillingRuntimeRefreshAt = float.PositiveInfinity;
    private ItemMgr itemManager;
    private SaveDataMgr saveManager;
    private ChunkMgr chunkManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCropOwners() => cropOwners.Clear();

    private void TrackCropOwner(int guid, Vector2Int local)
    {
        if (guid == 0 || cropOwners.TryGetValue(guid, out var existing) &&
            (existing.Owner != this || existing.Local != local))
            throw new InvalidOperationException("作物身份已由其他农业区块持有。" + guid);
        cropOwners[guid] = (this, local);
    }

    private void ForgetCropOwner(int guid, Vector2Int local)
    {
        if (cropOwners.TryGetValue(guid, out var owner) && owner.Owner == this && owner.Local == local)
            cropOwners.Remove(guid);
    }

    #endregion

    #region 绑定与持久化

    public void Bind(ChunkRuntime model)
    {
        Unbind();
        itemManager = ItemMgr.Instance;
        saveManager = SaveDataMgr.Instance;
        chunkManager = ChunkMgr.ExistingInstance;
        chunk = model;
        terrainOwner = GetComponentInParent<ChunkView>()?.GetComponentInChildren<ChunkTilemapRenderer>(true);
        if (terrainOwner != null) terrainOwner.BatchPresentationRebuilt += RefreshEntityPresentation;
        chunk.Terrain.Changed += HandleChanged;
        FarmlandSystem.TillingVisualChanged += HandleTillingVisualChanged;
        bindingInitialState = true;
        try
        {
            // 临时锄地按活跃格恢复；开采伤害层缺失时完全跳过逐格初始化。
            FarmlandSystem.CollectTillingVisualCells(chunk.Address, tillingCellBuffer);
            for (int i = 0; i < tillingCellBuffer.Count; i++)
            {
                Vector2Int local = tillingCellBuffer[i];
                if ((uint)local.x < (uint)chunk.Terrain.Width && (uint)local.y < (uint)chunk.Terrain.Height)
                    RefreshTillingOverlay(local, chunk.Terrain.GetCell(local.x, local.y));
            }

            if (chunk.Terrain.TryGetEnvironmentLayer(TileBuildingSystem.RuntimeDamageLayerId,
                    out ReadOnlyMemory<float> damageLayer))
            {
                ReadOnlySpan<float> damage = damageLayer.Span;
                int width = chunk.Terrain.Width;
                for (int index = 0; index < damage.Length; index++)
                {
                    if (damage[index] <= 0f) continue;
                    Vector2Int local = new(index % width, index / width);
                    RefreshGroundHarvestOverlay(local, chunk.Terrain.GetCell(local.x, local.y));
                }
            }
        }
        finally
        {
            tillingCellBuffer.Clear();
            bindingInitialState = false;
        }

        enabled = tillingOverlays.Count != 0 || hasActiveFade;

        foreach (var saved in saveManager.GetAgricultureCells(chunk.Address))
        {
            if (saved.Crop != null) RestoreSavedCrop(saved, GameNetwork.HasStateAuthority);
        }
    }

    private void RestoreSavedCrop(AgricultureCellSaveData saved, bool catchUp)
    {
        ItemData data = MemoryPack.MemoryPackSerializer.Deserialize<ItemData>(
            MemoryPack.MemoryPackSerializer.Serialize(saved.Crop));
        Vector2Int worldCell = saved.LocalPosition + Origin;
        CropWorldRelocation.RelocateSavedCrop(data, worldCell);
        if (!GameNetwork.HasStateAuthority && cropOwners.TryGetValue(data.Guid, out var owner) &&
            (owner.Owner != this || owner.Local != saved.LocalPosition))
        {
            if (owner.Owner.GetBoundCropDefinition(owner.Local) != data.IDName)
                throw new InvalidOperationException("农业快照与原作物定义不一致。" + data.Guid);
            if (!owner.Owner.TransferCropTo(owner.Local, this, saved.LocalPosition))
                throw new InvalidOperationException("已登记作物的源区块无法移交。" + data.Guid);
            ApplyAuthoritativeCropSnapshotAt(saved.LocalPosition, data);
            return;
        }
        if (GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out var definition) && definition.UsesResourceEntities)
        {
            RestoreEntityCrop(worldCell, definition, data, false);
            return;
        }
        Item crop = itemManager.InstantiateItem(data,
            new Vector3(worldCell.x + .5f, worldCell.y + .5f), parent: gameObject);
        crop.Load();
        RegisterCrop(worldCell, crop);
        if (!catchUp) return;
        foreach (Module module in crop.Mods.Values)
        {
            if (crop.DestructionHandled) break;
            if (module is IWorldTimePlant plant) plant.CatchUpToWorldTime();
        }
    }

    #region 普通农业权威快照应用
    private sealed class ReplicaCropMove
    {
        internal ChunkAgricultureRenderer Source, Target;
        internal Vector2Int SourceLocal, TargetLocal;
        internal Item Item;
        internal ItemData EntityData;
        internal RuntimeItemDefinition EntityDefinition;
        internal Vector3 OriginalPosition, OriginalSavedPosition;
        internal bool Attached;
    }

    internal static bool HasBoundCrop(int guid) => cropOwners.ContainsKey(guid);

    private string GetBoundCropDefinition(Vector2Int local)
        => crops.TryGetValue(local, out Item item) ? item?.itemData?.IDName :
            entityCrops.TryGetValue(local, out NaturalEntityHandle handle) &&
            NaturalEntityEcsService.TryGetDefinition(handle, out RuntimeItemDefinition definition) ? definition.Id : null;

    // 同一批权威区块先迁移既有作物，再按各区块删除或创建，避免快照顺序改变身份。
    internal static void ApplyAuthoritativeCropSnapshotBatch(
        IReadOnlyList<(ChunkAgricultureRenderer Renderer, IReadOnlyList<AgricultureCellSaveData> Cells)> batches)
    {
        if (batches == null || batches.Count == 0) return;
        var desired = new Dictionary<int, (ChunkAgricultureRenderer Owner, Vector2Int Local, ItemData Data)>();
        foreach (var batch in batches)
        {
            if (batch.Renderer == null || batch.Renderer.chunk == null || batch.Renderer.unbinding)
                throw new InvalidOperationException("农业快照的区块尚未绑定。");
            var occupied = new HashSet<Vector2Int>();
            foreach (AgricultureCellSaveData cell in batch.Cells)
                if (cell.Crop != null && ((uint)cell.LocalPosition.x >= (uint)batch.Renderer.chunk.Terrain.Width ||
                    (uint)cell.LocalPosition.y >= (uint)batch.Renderer.chunk.Terrain.Height ||
                    !occupied.Add(cell.LocalPosition) || cell.Crop.Guid == 0 ||
                    !desired.TryAdd(cell.Crop.Guid, (batch.Renderer, cell.LocalPosition, cell.Crop))))
                    throw new InvalidOperationException("农业快照包含无效或重复的作物身份与坐标。");
        }
        var moves = new List<ReplicaCropMove>();
        foreach (var pair in desired)
        {
            if (!cropOwners.TryGetValue(pair.Key, out var owner) ||
                owner.Owner == pair.Value.Owner && owner.Local == pair.Value.Local) continue;
            string definition = owner.Owner.GetBoundCropDefinition(owner.Local);
            if (definition != pair.Value.Data.IDName)
                throw new InvalidOperationException("农业快照与原作物定义不一致。" + pair.Key);
            moves.Add(new ReplicaCropMove { Source = owner.Owner, SourceLocal = owner.Local,
                Target = pair.Value.Owner, TargetLocal = pair.Value.Local });
        }
        try
        {
            foreach (ReplicaCropMove move in moves) move.Source.DetachReplicaCropForTransfer(move);
            foreach (var batch in batches)
            {
                var wanted = new Dictionary<Vector2Int, int>();
                foreach (AgricultureCellSaveData cell in batch.Cells)
                    if (cell.Crop != null) wanted.Add(cell.LocalPosition, cell.Crop.Guid);
                using var scratch = UnityEngine.Pool.ListPool<Vector2Int>.Get(out var removed);
                foreach (var pair in batch.Renderer.crops)
                    if (!wanted.TryGetValue(pair.Key, out int guid) || pair.Value == null ||
                        pair.Value.itemData == null || pair.Value.itemData.Guid != guid)
                        removed.Add(pair.Key);
                foreach (Vector2Int local in removed) batch.Renderer.RemoveReplicaCropAt(local);
                removed.Clear();
                foreach (var pair in batch.Renderer.entityCrops)
                    if (!wanted.TryGetValue(pair.Key, out int guid) ||
                        !batch.Renderer.entityCropGuids.TryGetValue(pair.Value.Id, out int current) || current != guid)
                        removed.Add(pair.Key);
                foreach (Vector2Int local in removed) batch.Renderer.RemoveReplicaCropAt(local);
            }
            foreach (ReplicaCropMove move in moves)
            {
                move.Target.AttachReplicaCropForTransfer(move, restoreSource: false);
            }
        }
        catch (Exception failure)
        {
            try
            {
                for (int i = moves.Count - 1; i >= 0; i--)
                    if (moves[i].Attached) moves[i].Target.DetachReplicaCropForTransfer(moves[i], fromTarget: true);
                for (int i = moves.Count - 1; i >= 0; i--)
                    if (moves[i].Item != null || moves[i].EntityData != null)
                        moves[i].Source.AttachReplicaCropForTransfer(moves[i], restoreSource: true);
            }
            catch (Exception rollback) { throw new AggregateException("农业快照移交及回滚失败。", failure, rollback); }
            throw;
        }
        foreach (var batch in batches) batch.Renderer.ApplyAuthoritativeCropSnapshots(batch.Cells);
    }

    private void DetachReplicaCropForTransfer(ReplicaCropMove move, bool fromTarget = false)
    {
        Vector2Int local = fromTarget ? move.TargetLocal : move.SourceLocal;
        if (crops.Remove(local, out Item item) && item != null)
        {
            if (!fromTarget)
            {
                move.Item = item;
                move.OriginalPosition = item.transform.position;
                move.OriginalSavedPosition = item.itemData.transform.position;
            }
            ForgetCropOwner(item.itemData.Guid, local);
            item.OnItemDestroy -= HandleCropDestroyed;
            item.transform.SetParent(null, true);
            return;
        }
        if (!entityCrops.TryGetValue(local, out NaturalEntityHandle handle) ||
            !NaturalEntityEcsService.TryGetDefinition(handle, out RuntimeItemDefinition definition) ||
            !NaturalEntityEcsService.TryTakeSnapshotAndRemove(handle, out ItemData data))
            throw new InvalidOperationException("农业快照的源作物无法移交。");
        entityCrops.Remove(local);
        entityCropCells.Remove(handle.Id);
        entityCropGuids.Remove(handle.Id);
        ForgetCropOwner(data.Guid, local);
        if (!fromTarget)
        {
            move.EntityDefinition = definition;
            move.EntityData = data;
            move.OriginalSavedPosition = data.transform.position;
        }
    }

    private void AttachReplicaCropForTransfer(ReplicaCropMove move, bool restoreSource)
    {
        Vector2Int local = restoreSource ? move.SourceLocal : move.TargetLocal;
        if (crops.ContainsKey(local) || entityCrops.ContainsKey(local))
            throw new InvalidOperationException("农业快照移交目标已有作物。");
        Vector2Int cell = Origin + local;
        Vector3 position = new(cell.x + .5f, cell.y + .5f);
        using var scope = GameplayWorldRegistry.UseWorld(chunkManager.WorldRuntime.WorldId);
        if (move.Item != null)
        {
            GameplayWorldRegistry.AssignItemToWorld(move.Item, chunkManager.WorldRuntime.WorldId);
            move.Item.transform.position = restoreSource ? move.OriginalPosition : WorldLocalPresentation.ProjectPosition(position);
            move.Item.itemData.transform.position = restoreSource ? move.OriginalSavedPosition : position;
            CropWorldRelocation.RelocateLiveCrop(move.Item, cell);
            RegisterCrop(cell, move.Item);
            move.Attached = true;
            itemManager.NotifyRuntimeItemMoved(move.Item);
        }
        else
        {
            move.EntityData.transform.position = restoreSource ? move.OriginalSavedPosition : position;
            CropWorldRelocation.RelocateSavedCrop(move.EntityData, cell);
            RestoreEntityCrop(cell, move.EntityDefinition, move.EntityData, false);
            move.Attached = true;
        }
    }

    // 网络只更新原有作物身份和状态，不解绑整块世界或重建相同植株。
    internal void ApplyAuthoritativeCropSnapshots(IReadOnlyList<AgricultureCellSaveData> cells)
    {
        if (GameNetwork.HasStateAuthority || chunk == null || unbinding) return;
        string worldKey = chunkManager?.WorldRuntime?.WorldId;
        if (string.IsNullOrWhiteSpace(worldKey)) throw new InvalidOperationException("农业快照缺少所属世界。");
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        var wanted = new Dictionary<Vector2Int, ItemData>();
        foreach (AgricultureCellSaveData saved in cells)
        {
            if ((uint)saved.LocalPosition.x >= (uint)chunk.Terrain.Width ||
                (uint)saved.LocalPosition.y >= (uint)chunk.Terrain.Height)
                throw new ArgumentException("农业快照坐标越过所属区块。");
            if (saved.Crop != null && !wanted.TryAdd(saved.LocalPosition, saved.Crop))
                throw new ArgumentException("农业快照包含重复作物格。");
        }
        using (UnityEngine.Pool.ListPool<Vector2Int>.Get(out var removed))
        {
            foreach (var pair in crops)
                if (!wanted.TryGetValue(pair.Key, out ItemData data) || pair.Value == null ||
                    pair.Value.itemData.Guid != data.Guid || pair.Value.itemData.IDName != data.IDName) removed.Add(pair.Key);
            foreach (Vector2Int local in removed) RemoveReplicaCropAt(local);
            removed.Clear();
            foreach (var pair in entityCrops)
                if (!wanted.TryGetValue(pair.Key, out ItemData data) ||
                    !NaturalEntityEcsService.TryCapture(pair.Value, out ItemData current) ||
                    current.Guid != data.Guid || current.IDName != data.IDName) removed.Add(pair.Key);
            foreach (Vector2Int local in removed) RemoveReplicaCropAt(local);
        }
        foreach (AgricultureCellSaveData saved in cells)
        {
            ItemData data = saved.Crop;
            if (data == null) continue;
            Vector2Int local = saved.LocalPosition;
            if (crops.ContainsKey(local) || entityCrops.ContainsKey(local)) ApplyAuthoritativeCropSnapshotAt(local, data);
            else RestoreSavedCrop(saved, false);
        }
    }

    private void ApplyAuthoritativeCropSnapshotAt(Vector2Int local, ItemData data)
    {
        if (crops.TryGetValue(local, out Item crop))
        {
            if (ItemNetworkStateSerialization.HasCurrentRemoteAuthority(crop)) return;
            CropWorldRelocation.RelocateSavedCrop(data, Origin + local);
            if (!ItemNetworkStateSerialization.TrySerializeItemData(data, out byte[] payload) ||
                !ItemNetworkStateSerialization.Apply(crop, payload, true, true))
                throw new InvalidOperationException("普通作物快照未能原位应用：" + data.IDName);
        }
        else if (entityCrops.TryGetValue(local, out NaturalEntityHandle handle))
        {
            CropWorldRelocation.RelocateSavedCrop(data, Origin + local);
            chunk.Terrain.TryGetEnvironmentValue("temperature.celsius", local.x, local.y, out float temperature);
            NaturalEntityEcsService.ApplySnapshot(handle, data, temperature);
        }
    }

    private void RemoveReplicaCropAt(Vector2Int local)
    {
        using var scope = GameplayWorldRegistry.UseWorld(chunkManager.WorldRuntime.WorldId);
        if (crops.Remove(local, out Item crop) && crop != null)
        {
            ForgetCropOwner(crop.itemData.Guid, local);
            crop.OnItemDestroy -= HandleCropDestroyed;
            itemManager.DespawnItem(crop, false);
        }
        if (entityCrops.TryGetValue(local, out NaturalEntityHandle handle)) RollbackEntityCrop(handle);
    }
    #endregion

    public void Unbind()
    {
        if (chunk == null)
            return;
        unbinding = true;
        using (CropCaptureMarker.Auto())
            CaptureStateForUnbind();
        chunk.Terrain.Changed -= HandleChanged;
        FarmlandSystem.TillingVisualChanged -= HandleTillingVisualChanged;
        ClearTillingProgressForOwnedRuntimeChunk();
        if (terrainOwner != null) terrainOwner.BatchPresentationRebuilt -= RefreshEntityPresentation;
        using (CropDespawnMarker.Auto())
        {
            foreach (Item crop in crops.Values)
            {
                if (crop == null)
                    continue;
                if (crop.itemData != null && cropOwners.TryGetValue(crop.itemData.Guid, out var owner) && owner.Owner == this)
                    cropOwners.Remove(crop.itemData.Guid);
                crop.OnItemDestroy -= HandleCropDestroyed;
                if (itemManager != null)
                    itemManager.DespawnItem(crop, saveData: false);
            }
        }
        crops.Clear();
        foreach (NaturalEntityHandle handle in entityCrops.Values) NaturalEntityEcsService.Remove(handle);
        foreach (var pair in entityCropGuids)
            if (entityCropCells.TryGetValue(pair.Key, out Vector2Int local)) ForgetCropOwner(pair.Value, local);
        entityCrops.Clear();
        entityCropCells.Clear();
        entityCropGuids.Clear();
        terrainOwner = null;
        using (OverlayClearMarker.Auto())
        {
            foreach (ProgressOverlay overlay in tillingOverlays.Values)
                DisposeOverlay(overlay);
            foreach (ProgressOverlay overlay in groundHarvestOverlays.Values)
                DisposeOverlay(overlay);
        }
        tillingOverlays.Clear();
        groundHarvestOverlays.Clear();
        tillingCellBuffer.Clear();
        hasActiveFade = false;
        nextTillingRuntimeRefreshAt = float.PositiveInfinity;
        chunk = null;
        unbinding = false;
        enabled = false;
    }

    /// <summary>自动保存与区块解绑共用抓取入口。</summary>
    public void CaptureState()
    {
        if (!GameNetwork.HasStateAuthority || chunk == null || applicationQuitting ||
            saveManager == null || chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown)
            return;

        CaptureItemCropStates();
        entityCropCaptureBuffer.Clear();
        entityCropCaptureBuffer.AddRange(entityCrops);
        for (int i = 0; i < entityCropCaptureBuffer.Count; i++)
        {
            KeyValuePair<Vector2Int, NaturalEntityHandle> pair = entityCropCaptureBuffer[i];
            NaturalEntityEcsService.PrepareForCapture(pair.Value);
            if (NaturalEntityEcsService.TryCapture(pair.Value, out ItemData snapshot))
                saveManager.RecordCultivatedCropData(chunk.Address, pair.Key, snapshot);
        }
        entityCropCaptureBuffer.Clear();
    }

    /// <summary>GameObject 作物解绑后会进对象池，因此仍复制其当前数据。</summary>
    private void CaptureItemCropStates()
    {
        foreach (var pair in crops)
        {
            if (pair.Value == null || pair.Value.DestructionHandled)
                continue;
            pair.Value.Save();
            saveManager.RecordCultivatedCrop(chunk.Address, pair.Key, pair.Value);
        }
    }

    /// <summary>终端解绑直接移交 Entity 作物快照，避免先深拷贝再立刻销毁实体。</summary>
    private void CaptureStateForUnbind()
    {
        bool canCapture = GameNetwork.HasStateAuthority && chunk != null && !applicationQuitting &&
                          saveManager != null && chunkManager != null && !chunkManager.IsWorldRuntimeShuttingDown;
        if (canCapture)
            CaptureItemCropStates();

        entityCropCaptureBuffer.Clear();
        entityCropCaptureBuffer.AddRange(entityCrops);
        for (int i = 0; i < entityCropCaptureBuffer.Count; i++)
        {
            KeyValuePair<Vector2Int, NaturalEntityHandle> pair = entityCropCaptureBuffer[i];
            if (canCapture && NaturalEntityEcsService.TryTakeSnapshotAndRemove(pair.Value, out ItemData snapshot))
                saveManager.RecordCultivatedCropData(chunk.Address, pair.Key, snapshot);
            else
                NaturalEntityEcsService.Remove(pair.Value);
        }
        entityCropCaptureBuffer.Clear();
    }

    public void RegisterCrop(Vector2Int worldCell, Item crop)
    {
        Vector2Int local = worldCell - Origin;
        if (crops.ContainsKey(local) || entityCrops.ContainsKey(local))
            throw new InvalidOperationException("种植格已经有作物。");
        TrackCropOwner(crop.itemData.Guid, local);
        try { crops.Add(local, crop); }
        catch { ForgetCropOwner(crop.itemData.Guid, local); throw; }
        crop.PersistenceOwner = this;
        crop.transform.SetParent(transform, true);
        crop.OnItemDestroy += HandleCropDestroyed;
    }

    #region 普通作物跨世界移交
    // 普通植株迁移真实 Item，数据植株沿原 Entity 后端移交快照，不产生收获或死亡。
    internal bool TransferCropTo(Vector2Int local, ChunkAgricultureRenderer destination, Vector2Int targetLocal)
    {
        if (destination == null || destination.chunk == null || destination.unbinding || chunk == null || unbinding)
            throw new InvalidOperationException("作物移交两端的区块尚未就绪。");
        if (destination.crops.ContainsKey(targetLocal) || destination.entityCrops.ContainsKey(targetLocal))
            throw new InvalidOperationException("目标地格已有作物。");
        if (!crops.ContainsKey(local) && !entityCrops.ContainsKey(local)) return false;
        var move = new ReplicaCropMove { Source = this, SourceLocal = local, Target = destination, TargetLocal = targetLocal };
        try
        {
            DetachReplicaCropForTransfer(move);
            destination.AttachReplicaCropForTransfer(move, restoreSource: false);
        }
        catch (Exception failure)
        {
            try
            {
                if (move.Attached) destination.DetachReplicaCropForTransfer(move, fromTarget: true);
                if (move.Item != null || move.EntityData != null) AttachReplicaCropForTransfer(move, restoreSource: true);
            }
            catch (Exception rollback) { throw new AggregateException("作物移交及回滚失败。", failure, rollback); }
            throw;
        }
        return true;
    }
    #endregion

    private Vector2Int Origin => new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);

    #region Entity 播种与收获

    /// <summary>创建植株后由种子入口提交扣料；失败只回滚实体，不制造收获或死亡事件。</summary>
    public NaturalEntityHandle CreateEntityCrop(Vector2Int worldCell, RuntimeItemDefinition definition)
    {
        if (chunk == null || unbinding || !GameNetwork.HasStateAuthority)
            throw new InvalidOperationException("农业区块尚未就绪。");
        Vector3 center = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (!chunkManager.TryGetRuntimeTerrainTile(center, out var sample))
            throw new InvalidOperationException("种植格没有可用的地形数据。");
        FarmlandSystem.EnsureSoilState(sample);
        ItemData data = definition.CreateItemData();
        data.Guid = Guid.NewGuid().GetHashCode() & int.MaxValue;
        if (data.Guid == 0) data.Guid = 1;
        data.transform.position = center;
        data.transform.rotation = Quaternion.identity;
        data.transform.scale = Vector3.one;
        return RestoreEntityCrop(worldCell, definition, data, true);
    }

    private NaturalEntityHandle RestoreEntityCrop(Vector2Int worldCell, RuntimeItemDefinition definition,
        ItemData data, bool newlyPlanted)
    {
        Vector2Int local = worldCell - Origin;
        if (entityCrops.ContainsKey(local) || crops.ContainsKey(local))
            throw new InvalidOperationException("种植格已经有作物。");
        chunk.Terrain.TryGetEnvironmentValue("temperature.celsius", local.x, local.y, out float temperature);
        chunk.Terrain.TryGetEnvironmentValue("precipitation", local.x, local.y, out float precipitation);
        if (!NaturalEntityEcsService.TryRegister(definition, data.Guid, data.transform.position,
            temperature, precipitation, chunk.Address.DimensionId, data, out NaturalEntityHandle handle,
            newlyPlanted ? worldCell : null))
            throw new InvalidOperationException($"作物 {definition.Id} 未能建立 Entity，禁止回退 Item。");
        try
        {
            TrackCropOwner(data.Guid, local);
            entityCrops.Add(local, handle);
            entityCropCells.Add(handle.Id, local);
            entityCropGuids.Add(handle.Id, data.Guid);
            NaturalEntityEcsService.BindRemovalHandler(handle, HandleEntityCropRemoved);
            NaturalEntityEcsService.BindPresentation(handle, terrainOwner);
            return handle;
        }
        catch
        {
            entityCrops.Remove(local); entityCropCells.Remove(handle.Id); entityCropGuids.Remove(handle.Id);
            ForgetCropOwner(data.Guid, local);
            NaturalEntityEcsService.Remove(handle);
            throw;
        }
    }

    public void RollbackEntityCrop(NaturalEntityHandle handle)
    {
        if (entityCropCells.Remove(handle.Id, out Vector2Int local))
        {
            entityCrops.Remove(local);
            if (entityCropGuids.Remove(handle.Id, out int guid)) ForgetCropOwner(guid, local);
        }
        NaturalEntityEcsService.Remove(handle);
    }

    public void CaptureEntityCrop(NaturalEntityHandle handle)
    {
        if (chunk != null && entityCropCells.TryGetValue(handle.Id, out Vector2Int local) &&
            NaturalEntityEcsService.TryCapture(handle, out ItemData snapshot))
            saveManager.RecordCultivatedCropData(chunk.Address, local, snapshot);
    }

    private void HandleEntityCropRemoved(NaturalEntityHandle handle)
    {
        if (chunk == null || applicationQuitting || chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown ||
            !entityCropCells.TryGetValue(handle.Id, out Vector2Int local) ||
            !entityCrops.TryGetValue(local, out var current) || current != handle) return;
        saveManager.RecordCultivatedCropData(chunk.Address, local, null);
        if (entityCropGuids.Remove(handle.Id, out int guid)) ForgetCropOwner(guid, local);
        entityCropCells.Remove(handle.Id); entityCrops.Remove(local);
    }

    private void RefreshEntityPresentation()
    {
        foreach (NaturalEntityHandle handle in entityCrops.Values)
        {
            if (NaturalEntityEcsService.TryGetDefinition(handle, out var old) &&
                GameRes.ExistingInstance.TryGetItemDefinition(old.Id, out var current) &&
                !NaturalEntityEcsService.TryRefreshDefinition(handle, current))
                Debug.LogError($"作物 {old.Id} 配置刷新失败，保留原 Entity。");
            NaturalEntityEcsService.BindPresentation(handle, terrainOwner);
        }
    }

    #endregion

    private void HandleCropDestroyed(Item crop)
    {
        if (unbinding || chunk == null || applicationQuitting ||
            chunkManager == null || chunkManager.IsWorldRuntimeShuttingDown)
            return;
        Vector2Int? found = null;
        foreach (var pair in crops)
            if (pair.Value == crop) { found = pair.Key; break; }
        if (!found.HasValue)
            return;
        crop.OnItemDestroy -= HandleCropDestroyed;
        crops.Remove(found.Value);
        if (crop.itemData != null) ForgetCropOwner(crop.itemData.Guid, found.Value);
        saveManager.RecordCultivatedCrop(chunk.Address, found.Value, null);
    }

    private void OnApplicationQuit() => applicationQuitting = true;

    // 旧表现只清仍归原管理器和原区块的临时进度，避免同地址的新世界被误清。
    private void ClearTillingProgressForOwnedRuntimeChunk()
    {
        if (chunk == null || ReferenceEquals(chunkManager, null) ||
            !chunkManager.TryGetChunkRuntime(chunk.Address, out ChunkRuntime ownedChunk) ||
            !ReferenceEquals(ownedChunk, chunk))
            return;
        string worldKey = !string.IsNullOrEmpty(chunkManager.OwnedWorldKey)
            ? chunkManager.OwnedWorldKey : chunkManager.WorldRuntime?.WorldId;
        if (string.IsNullOrEmpty(worldKey) ||
            !GameplayWorldRegistry.Worlds.TryGetValue(worldKey, out GameplayWorldRuntime registered) ||
            !ReferenceEquals(registered.Chunks, chunkManager))
            return;
        FarmlandSystem.ClearTillingProgress(chunk.Address);
    }

    private void OnDestroy()
    {
        foreach (var pair in crops)
            if (pair.Value != null && pair.Value.itemData != null) ForgetCropOwner(pair.Value.itemData.Guid, pair.Key);
        foreach (var pair in entityCropGuids)
            if (entityCropCells.TryGetValue(pair.Key, out Vector2Int local)) ForgetCropOwner(pair.Value, local);
        FarmlandSystem.TillingVisualChanged -= HandleTillingVisualChanged;
        ClearTillingProgressForOwnedRuntimeChunk();
    }

    #endregion

    #region 地块渐变

    private void HandleChanged(ChunkTerrainChanged change)
    {
        // 渐显读取采挖损伤与已挖层数，水肥变化由作物模拟自身消费。
        if (change.Kind == TerrainChangeKind.Environment &&
            !change.AffectsEnvironmentLayer(TileBuildingSystem.RuntimeDamageLayerId) &&
            !change.AffectsEnvironmentLayer(GroundTileLayerSystem.DepthLayerId))
            return;
        if (change.Kind == TerrainChangeKind.Cell || change.Kind == TerrainChangeKind.TileStack ||
            change.Kind == TerrainChangeKind.Environment)
        {
            int width = chunk.Terrain.Width;
            foreach (int index in change.GetCellIndices(width))
                RefreshCell(new Vector2Int(index % width, index / width));
        }
    }

    private void Update()
    {
        RefreshTillingRuntimeOverlays();
        if (hasActiveFade)
        {
            float alphaStep = Time.deltaTime / Mathf.Max(0.01f, tillingFadeDuration);
            hasActiveFade = FadeOverlays(tillingOverlays.Values, alphaStep) |
                            FadeOverlays(groundHarvestOverlays.Values, alphaStep);
        }

        // 无锄地进度和渐变时停止空闲区块的 Unity Update，事件仍能随时唤醒。
        if (!hasActiveFade && tillingOverlays.Count == 0)
            enabled = false;
    }

    private static bool FadeOverlays(IEnumerable<ProgressOverlay> overlays, float alphaStep)
    {
        bool stillFading = false;
        foreach (ProgressOverlay overlay in overlays)
        {
            if (overlay?.Renderer == null)
                continue;

            Color color = overlay.Renderer.color;
            float alpha = Mathf.MoveTowards(color.a, overlay.TargetAlpha, alphaStep);
            if (!Mathf.Approximately(alpha, color.a))
            {
                color.a = alpha;
                overlay.Renderer.color = color;
            }

            if (!Mathf.Approximately(alpha, overlay.TargetAlpha))
                stillFading = true;
        }

        return stillFading;
    }

    private void RefreshTillingRuntimeOverlays()
    {
        if (chunk == null || tillingOverlays.Count == 0)
        {
            nextTillingRuntimeRefreshAt = float.PositiveInfinity;
            return;
        }

        // 锄地结束后的固定保持阶段，无论有多少覆盖层都不需要再查农田状态。
        // 当任意格进入真实衰减或透明度收尾时，恢复每帧读取直到该阶段结束。
        if (!IsTillingRuntimeRefreshDue(Time.time, nextTillingRuntimeRefreshAt))
            return;

        using var refreshScope = TillingRefreshMarker.Auto();
        nextTillingRuntimeRefreshAt = float.PositiveInfinity;
        tillingCellBuffer.Clear();
        foreach (Vector2Int local in tillingOverlays.Keys)
            tillingCellBuffer.Add(local);
        for (int i = 0; i < tillingCellBuffer.Count; i++)
        {
            Vector2Int local = tillingCellBuffer[i];
            RefreshTillingOverlay(local, chunk.Terrain.GetCell(local.x, local.y));
        }
        tillingCellBuffer.Clear();
    }

    /// <summary>保持阶段只比较时间，不重复查询农田字典与地块数据。</summary>
    internal static bool IsTillingRuntimeRefreshDue(float now, float nextRefreshAt) => now >= nextRefreshAt;

    private void HandleTillingVisualChanged(RuntimeWorldAddress address, Vector2Int local)
    {
        if (chunk == null || !chunk.Address.Equals(address) ||
            (uint)local.x >= (uint)chunk.Terrain.Width || (uint)local.y >= (uint)chunk.Terrain.Height)
            return;
        RefreshTillingOverlay(local, chunk.Terrain.GetCell(local.x, local.y));
    }

    /// <summary>正式地形变化只负责收尾临时锄地表现，并刷新铲挖渐显。</summary>
    private void RefreshCell(Vector2Int local)
    {
        TerrainCell cell = chunk.Terrain.GetCell(local.x, local.y);
        RefreshTillingOverlay(local, cell);
        RefreshGroundHarvestOverlay(local, cell);
    }

    /// <summary>锄地进度只来自当前会话临时状态；停工十秒后会逐渐退回底下原本的草地。</summary>
    private void RefreshTillingOverlay(Vector2Int local, TerrainCell cell)
    {
        if (FarmlandSystem.IsFarmland(cell) || cell.BlockingTileId != 0)
        {
            if (tillingOverlays.Remove(local, out ProgressOverlay previous))
                DisposeOverlay(previous);
            return;
        }

        float progress = FarmlandSystem.GetTillingVisualProgress(
            chunk.Address, local, cell.GroundTileId, out float nextRefreshAt);
        if (progress <= 0f)
        {
            if (!tillingOverlays.TryGetValue(local, out ProgressOverlay fading))
                return;
            SetOverlayTargetAlpha(fading, 0f);
            if (fading.Renderer == null || fading.Renderer.color.a <= 0.001f)
            {
                tillingOverlays.Remove(local);
                DisposeOverlay(fading);
            }
            else
            {
                // 状态已退完时继续驱动最后一段透明度动画，然后释放闲置覆盖层。
                nextTillingRuntimeRefreshAt = Time.time;
            }
            return;
        }

        nextTillingRuntimeRefreshAt = Mathf.Min(nextTillingRuntimeRefreshAt, nextRefreshAt);
        if (!bindingInitialState)
            enabled = true;
        if (!tillingOverlays.TryGetValue(local, out ProgressOverlay overlay))
        {
            if (!TryResolveFarmlandVisual(out Sprite sprite, out Color color, out Matrix4x4 tileTransform))
                return;
            overlay = CreateOverlay("TillingProgress", local, sprite, color, tileTransform);
            tillingOverlays.Add(local, overlay);
        }

        SetOverlayProgress(overlay, progress, useMinimumAlpha: false);
    }

    /// <summary>铲地时像锄地一样逐步显现挖完后的底层地块，不再额外画裂纹。</summary>
    private void RefreshGroundHarvestOverlay(Vector2Int local, TerrainCell cell)
    {
        float progress = chunk.Terrain.TryGetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
            local.x, local.y, out float value) ? Mathf.Clamp01(value) : 0f;
        if (progress <= 0f || cell.BlockingTileId != 0)
        {
            if (groundHarvestOverlays.Remove(local, out ProgressOverlay previous))
                DisposeOverlay(previous);
            return;
        }

        if (!TryResolveGroundHarvestVisual(local, cell.GroundTileId, out Sprite sprite, out Color color,
                out Matrix4x4 tileTransform))
        {
            if (groundHarvestOverlays.Remove(local, out ProgressOverlay previous))
                DisposeOverlay(previous);
            return;
        }

        if (!groundHarvestOverlays.TryGetValue(local, out ProgressOverlay overlay))
        {
            overlay = CreateOverlay("GroundHarvestProgress", local, sprite, color, tileTransform);
            groundHarvestOverlays.Add(local, overlay);
        }

        SetOverlayProgress(overlay, progress, useMinimumAlpha: true);
    }

    private ProgressOverlay CreateOverlay(string name, Vector2Int local, Sprite sprite, Color color,
        Matrix4x4 tileTransform)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);
        Vector3 offset = tileTransform.MultiplyPoint3x4(Vector3.zero);
        root.transform.localPosition = new Vector3(local.x + 0.5f + offset.x, local.y + 0.5f + offset.y, offset.z);
        root.transform.localRotation = tileTransform.rotation;
        root.transform.localScale = tileTransform.lossyScale;
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = progressMaterial;
        WorldRenderingManager.Instance.ApplyRendererSorting(renderer, WorldSortingManager.GroundMarkCategory);
        renderer.spriteSortPoint = SpriteSortPoint.Center;
        renderer.color = new Color(color.r, color.g, color.b, 0f);
        return new ProgressOverlay { Renderer = renderer };
    }

    private void SetOverlayProgress(ProgressOverlay overlay, float progress, bool useMinimumAlpha)
    {
        float minimumAlpha = useMinimumAlpha ? Mathf.Clamp01(minimumProgressAlpha) : 0f;
        float targetAlpha = Mathf.Lerp(minimumAlpha,
            Mathf.Clamp01(Mathf.Max(minimumAlpha, maximumProgressAlpha)), Mathf.Clamp01(progress));
        SetOverlayTargetAlpha(overlay, targetAlpha);
    }

    private void SetOverlayTargetAlpha(ProgressOverlay overlay, float targetAlpha)
    {
        overlay.TargetAlpha = Mathf.Clamp01(targetAlpha);
        if (overlay.Renderer == null)
            return;

        if (bindingInitialState)
        {
            Color color = overlay.Renderer.color;
            color.a = overlay.TargetAlpha;
            overlay.Renderer.color = color;
        }
        else if (!Mathf.Approximately(overlay.Renderer.color.a, overlay.TargetAlpha))
        {
            hasActiveFade = true;
            enabled = true;
        }
    }

    private static bool TryResolveFarmlandVisual(out Sprite sprite, out Color color,
        out Matrix4x4 tileTransform)
    {
        sprite = null;
        color = Color.white;
        tileTransform = Matrix4x4.identity;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null ||
            !resources.TryGetTileDefinition(FarmlandSystem.TileBlockId, out RuntimeTileDefinition farmland))
            return false;
        return TryResolveTileVisual(farmland, out sprite, out color, out tileTransform);
    }

    private bool TryResolveGroundHarvestVisual(Vector2Int local, int sourceTileId, out Sprite sprite, out Color color,
        out Matrix4x4 tileTransform)
    {
        sprite = null;
        color = Color.white;
        tileTransform = Matrix4x4.identity;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetTileDefinition(sourceTileId, out RuntimeTileDefinition source) ||
            source.GroundHarvest == null ||
            !GroundTileHarvestSystem.TryResolveHarvestResult(new RuntimeTerrainTileSample(chunk.Address,
                chunk.Terrain, default, local, chunk.Terrain.GetCell(local.x, local.y), sourceTileId),
                source, out RuntimeTileDefinition target))
            return false;

        if (!TryResolveTileVisual(target, out sprite, out color, out tileTransform)) return false;
        // 连续石层使用深色开采痕迹，避免上下层同图时看不出工作进度。
        if (source.RuntimeTileId == target.RuntimeTileId)
            color = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, color.a);
        return true;
    }

    private static bool TryResolveTileVisual(RuntimeTileDefinition definition, out Sprite sprite, out Color color,
        out Matrix4x4 tileTransform)
    {
        sprite = null;
        color = Color.white;
        tileTransform = Matrix4x4.identity;
        if (definition?.TileBase is not Tile tile || tile.sprite == null)
            return false;
        sprite = tile.sprite;
        color = tile.color;
        tileTransform = tile.transform;
        return true;
    }

    private static void DisposeOverlay(ProgressOverlay overlay)
    {
        if (overlay?.Renderer == null)
            return;
        Destroy(overlay.Renderer.gameObject);
    }

    #endregion
}
