using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 区块农业表现：按权威进度逐列显露耕地，同时管理玩家播种的作物生命周期。
/// 作物在自动保存和区块解绑时抓取快照，收获时清除快照；不复用自然生态或临时掉落登记。
/// </summary>
public sealed class ChunkAgricultureRenderer : MonoBehaviour, IChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private Sprite farmlandSprite; // 与最终地块一致的耕地精灵
    [SerializeField] private Material progressMaterial; // 支持顶点颜色与主纹理的地表材质
    [SerializeField] private string sortingLayerName = "Default"; // 与地表一致的排序层
    [SerializeField] private int sortingOrder; // 位于地表之上
    private readonly Dictionary<Vector2Int, MeshFilter> overlays = new();
    private readonly Dictionary<Vector2Int, Item> crops = new();
    private ChunkRuntime chunk;
    private bool unbinding;
    private bool applicationQuitting;
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
        for (int y = 0; y < chunk.Terrain.Height; y++)
            for (int x = 0; x < chunk.Terrain.Width; x++)
                RefreshCell(new Vector2Int(x, y));

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
        foreach (MeshFilter overlay in overlays.Values)
            DisposeOverlay(overlay);
        overlays.Clear();
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

    /// <summary>每次耕作按进度显露原图的一部分，直到真实地形替换为完整耕地。</summary>
    private void RefreshCell(Vector2Int local)
    {
        TerrainCell cell = chunk.Terrain.GetCell(local.x, local.y);
        float progress = FarmlandSystem.Read(chunk.Terrain, local, FarmlandSystem.ProgressLayer);
        bool valid = progress > 0f && !FarmlandSystem.IsFarmland(cell) && cell.BlockingTileId == 0 &&
            FarmlandSystem.Read(chunk.Terrain, local, FarmlandSystem.SourceLayer) == cell.GroundTileId;
        if (!valid)
        {
            if (overlays.Remove(local, out MeshFilter previous))
                DisposeOverlay(previous);
            return;
        }
        if (!overlays.TryGetValue(local, out MeshFilter filter))
        {
            var root = new GameObject("TillingProgress");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(local.x, local.y, 0f);
            filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = "TillingProgress" };
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = progressMaterial;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", farmlandSprite.texture);
            renderer.SetPropertyBlock(properties);
            overlays.Add(local, filter);
        }
        float width = Mathf.Ceil(Mathf.Clamp01(progress) * 16f) / 16f;
        Rect rect = farmlandSprite.textureRect;
        Vector2 textureSize = new(farmlandSprite.texture.width, farmlandSprite.texture.height);
        float u0 = rect.xMin / textureSize.x;
        float v0 = rect.yMin / textureSize.y;
        float u1 = (rect.xMin + rect.width * width) / textureSize.x;
        float v1 = rect.yMax / textureSize.y;
        Mesh mesh = filter.sharedMesh;
        mesh.vertices = new[] { Vector3.zero, new Vector3(width, 0), new Vector3(0, 1), new Vector3(width, 1) };
        mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u0, v1), new Vector2(u1, v1) };
        // 地表材质把 RGBA 当四向接触阴影遮罩，零值表示普通耕地边缘。
        mesh.colors = new[] { Color.clear, Color.clear, Color.clear, Color.clear };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
    }

    private static void DisposeOverlay(MeshFilter overlay)
    {
        if (overlay == null)
            return;
        Destroy(overlay.sharedMesh);
        Destroy(overlay.gameObject);
    }

    #endregion
}
