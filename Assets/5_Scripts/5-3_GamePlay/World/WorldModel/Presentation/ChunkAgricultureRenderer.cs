using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 区块农业表现：按权威进度透明渐显耕地，同时管理玩家播种的作物生命周期。
/// 作物在自动保存和区块解绑时抓取快照，收获时清除快照；不复用自然生态或临时掉落登记。
/// </summary>
public sealed class ChunkAgricultureRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private Sprite farmlandSprite; // 与最终地块一致的耕地精灵
    [SerializeField] private Material progressMaterial; // 支持 SpriteRenderer 透明度的耕地渐显材质
    [SerializeField] private string sortingLayerName = "Default"; // 与地表一致的排序层
    [SerializeField] private int sortingOrder; // 位于地表之上
    [SerializeField, Min(0.01f)] private float tillingFadeDuration = 0.18f;
    [SerializeField, Range(0f, 1f)] private float minimumProgressAlpha = 0.12f;
    [SerializeField, Range(0f, 1f)] private float maximumProgressAlpha = 0.9f;

    private sealed class TillingOverlay
    {
        public SpriteRenderer Renderer;
        public float TargetAlpha;
    }

    private readonly Dictionary<Vector2Int, TillingOverlay> overlays = new();
    private readonly Dictionary<Vector2Int, Item> crops = new();
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
        chunk.Terrain.Changed += HandleChanged;
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
        CaptureState();
        chunk.Terrain.Changed -= HandleChanged;
        foreach (Item crop in crops.Values)
        {
            if (crop == null)
                continue;
            crop.OnItemDestroy -= HandleCropDestroyed;
            if (itemManager != null)
                itemManager.DespawnItem(crop, saveData: false);
        }
        crops.Clear();
        foreach (TillingOverlay overlay in overlays.Values)
            DisposeOverlay(overlay);
        overlays.Clear();
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
        foreach (var pair in crops)
        {
            if (pair.Value == null || pair.Value.DestructionHandled)
                continue;
            pair.Value.Save();
            saveManager.RecordCultivatedCrop(chunk.Address, pair.Key, pair.Value);
        }
    }

    public void RegisterCrop(Vector2Int worldCell, Item crop)
    {
        Vector2Int local = worldCell - Origin;
        crops.Add(local, crop);
        crop.transform.SetParent(transform, true);
        crop.OnItemDestroy += HandleCropDestroyed;
    }

    private Vector2Int Origin => new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);

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
        if (!hasActiveFade || overlays.Count == 0)
            return;

        float alphaStep = Time.deltaTime / Mathf.Max(0.01f, tillingFadeDuration);
        bool stillFading = false;
        foreach (TillingOverlay overlay in overlays.Values)
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

        hasActiveFade = stillFading;
    }

    /// <summary>每次耕作提高整格耕地的目标透明度，并在短时间内平滑渐显。</summary>
    private void RefreshCell(Vector2Int local)
    {
        TerrainCell cell = chunk.Terrain.GetCell(local.x, local.y);
        float progress = FarmlandSystem.Read(chunk.Terrain, local, FarmlandSystem.ProgressLayer);
        bool valid = progress > 0f && !FarmlandSystem.IsFarmland(cell) && cell.BlockingTileId == 0 &&
            FarmlandSystem.Read(chunk.Terrain, local, FarmlandSystem.SourceLayer) == cell.GroundTileId;
        if (!valid)
        {
            if (overlays.Remove(local, out TillingOverlay previous))
                DisposeOverlay(previous);
            if (overlays.Count == 0)
                hasActiveFade = false;
            return;
        }

        float targetAlpha = Mathf.Lerp(
            Mathf.Clamp01(minimumProgressAlpha),
            Mathf.Clamp01(Mathf.Max(minimumProgressAlpha, maximumProgressAlpha)),
            Mathf.Clamp01(progress));

        if (!overlays.TryGetValue(local, out TillingOverlay overlay))
        {
            var root = new GameObject("TillingProgress");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(local.x + 0.5f, local.y + 0.5f, 0f);
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = farmlandSprite;
            renderer.sharedMaterial = progressMaterial;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            renderer.spriteSortPoint = SpriteSortPoint.Center;
            renderer.color = new Color(1f, 1f, 1f, bindingInitialState ? targetAlpha : 0f);
            overlay = new TillingOverlay { Renderer = renderer, TargetAlpha = targetAlpha };
            overlays.Add(local, overlay);
            if (!bindingInitialState)
                hasActiveFade = true;
            return;
        }

        overlay.TargetAlpha = targetAlpha;
        if (bindingInitialState && overlay.Renderer != null)
        {
            Color color = overlay.Renderer.color;
            color.a = targetAlpha;
            overlay.Renderer.color = color;
        }
        else if (overlay.Renderer != null && !Mathf.Approximately(overlay.Renderer.color.a, targetAlpha))
        {
            hasActiveFade = true;
        }
    }

    private static void DisposeOverlay(TillingOverlay overlay)
    {
        if (overlay?.Renderer == null)
            return;
        Destroy(overlay.Renderer.gameObject);
    }

    #endregion
}
