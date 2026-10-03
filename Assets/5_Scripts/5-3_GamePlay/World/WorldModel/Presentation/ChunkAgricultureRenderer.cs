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
    private readonly List<KeyValuePair<Vector2Int, NaturalEntityHandle>> entityCropCaptureBuffer = new();
    private ChunkTilemapRenderer terrainOwner;
    private static readonly ProfilerMarker CropCaptureMarker =
        new("FlatWorld.ChunkStreaming.CaptureCrops");
    private static readonly ProfilerMarker CropDespawnMarker =
        new("FlatWorld.ChunkStreaming.DespawnCrops");
    private static readonly ProfilerMarker OverlayClearMarker =
        new("FlatWorld.ChunkStreaming.ClearTillingOverlays");
    private ChunkRuntime chunk;
    private bool unbinding;
    private bool applicationQuitting;
    private bool bindingInitialState;
    private bool hasActiveFade;
    private ItemMgr itemManager;
    private SaveDataMgr saveManager;
    private ChunkMgr chunkManager;

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
            for (int y = 0; y < chunk.Terrain.Height; y++)
                for (int x = 0; x < chunk.Terrain.Width; x++)
                    RefreshCell(new Vector2Int(x, y));
        }
        finally
        {
            bindingInitialState = false;
        }

        foreach (var saved in saveManager.GetAgricultureCells(chunk.Address))
        {
            if (saved.Crop == null)
                continue;
            ItemData data = MemoryPack.MemoryPackSerializer.Deserialize<ItemData>(
                MemoryPack.MemoryPackSerializer.Serialize(saved.Crop));
            Vector2Int worldCell = saved.LocalPosition + Origin;
            if (GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out var definition) && definition.UsesResourceEntities)
            {
                RestoreEntityCrop(worldCell, definition, data, false);
                continue;
            }
            Item crop = itemManager.InstantiateItem(data,
                new Vector3(worldCell.x + 0.5f, worldCell.y + 0.5f), parent: gameObject);
            crop.Load();
            RegisterCrop(saved.LocalPosition + Origin, crop);
            // 作物已登记销毁回调，补算期间枯萎也会清除对应农业快照。
            foreach (Module module in crop.Mods.Values)
            {
                if (crop.DestructionHandled)
                    break;
                if (module is IWorldTimePlant plant)
                    plant.CatchUpToWorldTime();
            }
        }
    }

    public void Unbind()
    {
        if (chunk == null)
            return;
        unbinding = true;
        using (CropCaptureMarker.Auto())
            CaptureStateForUnbind();
        chunk.Terrain.Changed -= HandleChanged;
        FarmlandSystem.TillingVisualChanged -= HandleTillingVisualChanged;
        FarmlandSystem.ClearTillingProgress(chunk.Address);
        if (terrainOwner != null) terrainOwner.BatchPresentationRebuilt -= RefreshEntityPresentation;
        using (CropDespawnMarker.Auto())
        {
            foreach (Item crop in crops.Values)
            {
                if (crop == null)
                    continue;
                crop.OnItemDestroy -= HandleCropDestroyed;
                if (itemManager != null)
                    itemManager.DespawnItem(crop, saveData: false);
            }
        }
        crops.Clear();
        foreach (NaturalEntityHandle handle in entityCrops.Values) NaturalEntityEcsService.Remove(handle);
        entityCrops.Clear();
        entityCropCells.Clear();
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
        chunk = null;
        unbinding = false;
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
        crops.Add(local, crop);
        crop.transform.SetParent(transform, true);
        crop.OnItemDestroy += HandleCropDestroyed;
    }

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
            entityCrops.Add(local, handle);
            entityCropCells.Add(handle.Id, local);
            NaturalEntityEcsService.BindRemovalHandler(handle, HandleEntityCropRemoved);
            NaturalEntityEcsService.BindPresentation(handle, terrainOwner);
            return handle;
        }
        catch
        {
            entityCrops.Remove(local); entityCropCells.Remove(handle.Id);
            NaturalEntityEcsService.Remove(handle);
            throw;
        }
    }

    public void RollbackEntityCrop(NaturalEntityHandle handle)
    {
        if (entityCropCells.Remove(handle.Id, out Vector2Int local)) entityCrops.Remove(local);
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
        saveManager.RecordCultivatedCrop(chunk.Address, found.Value, null);
    }

    private void OnApplicationQuit() => applicationQuitting = true;

    private void OnDestroy()
    {
        FarmlandSystem.TillingVisualChanged -= HandleTillingVisualChanged;
        if (chunk != null)
            FarmlandSystem.ClearTillingProgress(chunk.Address);
    }

    #endregion

    #region 地块渐变

    private void HandleChanged(ChunkTerrainChanged change)
    {
        if (change.Kind == TerrainChangeKind.Cell || change.Kind == TerrainChangeKind.TileStack ||
            change.Kind == TerrainChangeKind.Environment)
            RefreshCell(new Vector2Int(change.LocalCell.X, change.LocalCell.Y));
    }

    private void Update()
    {
        RefreshTillingRuntimeOverlays();
        if (!hasActiveFade)
            return;

        float alphaStep = Time.deltaTime / Mathf.Max(0.01f, tillingFadeDuration);
        hasActiveFade = FadeOverlays(tillingOverlays.Values, alphaStep) |
                        FadeOverlays(groundHarvestOverlays.Values, alphaStep);
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
            return;

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

        float progress = FarmlandSystem.GetTillingVisualProgress(chunk.Address, local, cell.GroundTileId);
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
            return;
        }

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

        if (!TryResolveGroundHarvestVisual(cell.GroundTileId, out Sprite sprite, out Color color,
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
        WorldSortingManager.GetInstance().ApplyRenderer(renderer, WorldSortingManager.GroundMarkCategory);
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

    private static bool TryResolveGroundHarvestVisual(int sourceTileId, out Sprite sprite, out Color color,
        out Matrix4x4 tileTransform)
    {
        sprite = null;
        color = Color.white;
        tileTransform = Matrix4x4.identity;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetTileDefinition(sourceTileId, out RuntimeTileDefinition source) ||
            source.GroundHarvest == null ||
            !resources.TryGetTileDefinition(source.GroundHarvest.ReplacementTileId, out RuntimeTileDefinition target))
            return false;

        return TryResolveTileVisual(target, out sprite, out color, out tileTransform);
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
