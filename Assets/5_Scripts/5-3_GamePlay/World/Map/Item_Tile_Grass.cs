using System;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 旧版 TileBlock 的通用 Item 兼容壳。它仍被 Dirt、Sand、Stone 等旧 TileBlock Prefab 共用，
/// 但不再代表草层实体；草的状态和表现统一由 GrassLayerData、GrassDetailLayer 或 ChunkGrassRenderer 处理。
/// 旧石墙物品在这里适配新区块建筑系统，避免旧 Prefab 与新 ChunkTerrainData 脱节。
/// </summary>
public class Item_Tile_Grass : Item
{
    #region 旧物品到新区块建筑的兼容映射

    private const string LegacyStoneWallItemId = "TileItem_StoneWall";
    public const string RuntimeStoneWallTileBlockId = "TileBase_BuiltStoneWall";

    private BuildingShadow placementShadow;
    private GameController ownerController;
    private bool previewCreationFailed;

    /// <summary>旧石墙物品是否已经接入新区块的阻挡地块。</summary>
    public static bool TryResolveRuntimeTileBlockId(string itemId, out string tileBlockId)
    {
        tileBlockId = null;
        if (!string.Equals(itemId, LegacyStoneWallItemId, StringComparison.Ordinal))
            return false;

        tileBlockId = RuntimeStoneWallTileBlockId;
        return true;
    }

    /// <summary>供运行时诊断和实际路径检查确认预览对象已经生成。</summary>
    public bool HasPlacementPreview => placementShadow != null &&
                                       placementShadow.ShadowRenderer != null &&
                                       placementShadow.ShadowRenderer.enabled;

    /// <summary>返回当前旧石墙物品使用的建筑虚影。</summary>
    public BuildingShadow PlacementShadow => placementShadow;

    #endregion

    #region 物品数据

    [SerializeField]
    private BlockData data = new BlockData();
    public override ItemData itemData => data;

    protected override void SetItemData(ItemData value)
    {
        data = RequireData<BlockData>(value);
    }

    [SerializeField]
    private TileData_Grass _tileData;
    public TileData TileData { get => _tileData; set => _tileData = (TileData_Grass)value; }

    #endregion

    #region 手持预览与放置

    public override void Act()
    {
        if (!TryResolveRuntimeTileBlockId(itemData?.IDName, out string tileBlockId))
        {
            base.Act();
            return;
        }

        if (!TryRefreshPlacementPreview(out Vector3 placement, out string previewReason))
        {
            Debug.LogWarning($"[旧地块建筑] 石墙预览未就绪：{previewReason}", this);
            return;
        }

        if (!TileBuildingSystem.TryPlace(placement, tileBlockId,
                out TileBuildingCell _, out string reason))
        {
            Debug.LogWarning($"[旧地块建筑] 石墙放置失败：{reason}", this);
            placementShadow?.UpdateColor(true);
            return;
        }

        ConsumeOneStoneWallItem();
    }

    /// <summary>刷新手持石墙的格心预览；返回值只表示预览对象和指针坐标是否可用。</summary>
    public bool TryRefreshPlacementPreview(out Vector3 placement, out string reason)
    {
        placement = default;
        reason = null;
        if (!InHand)
        {
            reason = "物品不在手持状态";
            return false;
        }

        EnsurePlacementShadow();
        if (placementShadow == null)
        {
            reason = "BuildingShadow 创建失败";
            return false;
        }

        Camera placementCamera = ResolvePlacementCamera();
        if (placementCamera == null)
        {
            reason = "主相机尚未就绪";
            return false;
        }

        placement = GetPointerPlacement(placementCamera);
        placementShadow.transform.position = placement;
        // 旧 TileItem 没有召唤器距离配置，预览必须保持可见；合法性用颜色区分。
        placementShadow.UpdateAlpha(1f);
        bool canPlace = TryResolveRuntimeTileBlockId(itemData?.IDName, out string tileBlockId) &&
                        TileBuildingSystem.CanPlace(placement, tileBlockId, out reason);
        placementShadow.UpdateColor(!canPlace);
        return true;
    }

    private void Update()
    {
        if (!TryResolveRuntimeTileBlockId(itemData?.IDName, out _))
            return;

        if (!InHand)
        {
            DestroyPlacementShadow();
            previewCreationFailed = false;
            return;
        }

        TryRefreshPlacementPreview(out _, out _);
    }

    private void OnDisable()
    {
        DestroyPlacementShadow();
        previewCreationFailed = false;
    }

    private void EnsurePlacementShadow()
    {
        if (placementShadow != null || previewCreationFailed || GameRes.Instance == null)
            return;

        GameObject shadowObject = null;
        try
        {
            shadowObject = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            placementShadow = shadowObject != null
                ? shadowObject.GetComponentInChildren<BuildingShadow>(true)
                : null;

            SpriteRenderer sourceRenderer = Sprite != null
                ? Sprite
                : GetComponentInChildren<SpriteRenderer>(true);
            if (placementShadow == null || sourceRenderer == null)
                throw new MissingComponentException("BuildingShadow 或旧石墙 SpriteRenderer 配置不完整");

            placementShadow.InitShadow(sourceRenderer, transform, ResolvePreviewFootprint());
        }
        catch (Exception exception)
        {
            previewCreationFailed = true;
            if (shadowObject != null)
                Destroy(shadowObject);
            placementShadow = null;
            Debug.LogError($"[旧地块建筑预览] 创建失败：{exception.Message}", this);
        }
    }

    private Camera ResolvePlacementCamera()
    {
        if (ownerController == null && Owner != null)
        {
            ownerController = Owner.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
            ownerController ??= Owner.GetComponent<GameController>();
        }

        return ownerController?._mainCamera != null ? ownerController._mainCamera : Camera.main;
    }

    private Vector3 GetPointerPlacement(Camera placementCamera)
    {
        Vector2 screenPosition = ownerController != null
            ? ownerController.GetPointerScreenPosition()
            : (Vector2)Input.mousePosition;
        Vector3 worldPosition = placementCamera.ScreenToWorldPoint(new Vector3(
            screenPosition.x,
            screenPosition.y,
            Mathf.Abs(placementCamera.transform.position.z)));
        worldPosition.z = 0f;
        worldPosition = WorldTopologyRuntime.NormalizePosition(worldPosition);
        return new Vector3(
            Mathf.FloorToInt(worldPosition.x) + 0.5f,
            Mathf.FloorToInt(worldPosition.y) + 0.5f,
            0f);
    }

    private Bounds ResolvePreviewFootprint()
    {
        BoxCollider2D sourceCollider = GetComponentInChildren<BoxCollider2D>(true);
        if (sourceCollider == null)
            return new Bounds(Vector3.zero, Vector3.one);

        Vector2 half = sourceCollider.size * 0.5f;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 localPoint = sourceCollider.offset +
                    Vector2.Scale(half, new Vector2(x, y));
                Vector3 rootPoint = transform.InverseTransformPoint(
                    sourceCollider.transform.TransformPoint(localPoint));
                min = Vector2.Min(min, rootPoint);
                max = Vector2.Max(max, rootPoint);
            }
        }

        Vector2 size = max - min;
        return new Bounds(
            (min + max) * 0.5f,
            new Vector3(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y), 0.1f));
    }

    private void ConsumeOneStoneWallItem()
    {
        if (itemData?.Stack == null)
            return;

        Item owner = Owner;
        Inventory_HotBar hotBar = owner?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        ItemSlot selectedSlot = hotBar?.CurrentSelectItemSlot;
        itemData.Stack.Amount = Mathf.Max(0f, itemData.Stack.Amount - 1f);
        bool depleted = itemData.Stack.Amount <= 0f;
        if (depleted && selectedSlot != null && ReferenceEquals(selectedSlot.itemData, itemData))
        {
            selectedSlot.ClearData();
            selectedSlot.RefreshUI();
        }

        OnUIRefresh?.Invoke();
        if (owner != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(owner);

        if (!depleted)
        {
            Save();
            return;
        }

        DestroyPlacementShadow();
        if (ItemMgr.Instance != null)
            ItemMgr.Instance.DespawnItem(this, false);
        else
            DestroySelf();
    }

    private void DestroyPlacementShadow()
    {
        if (placementShadow == null)
            return;

        Destroy(placementShadow.gameObject);
        placementShadow = null;
    }

    #endregion

    #region 旧 Map 兼容接口

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        worldPos = WorldTopologyRuntime.NormalizePosition(worldPos);

        // 获取 MapCore 对象和 Map 脚本
        GameObject mapCore = GameObject.FindGameObjectWithTag("MapCore");
        Map mapCoreScript = mapCore.GetComponent<Map>();

        // 使用 Map 脚本中的 tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // 把世界坐标转换为格子坐标
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

        // 设置 TileData 的坐标
        tileData.position = cellPos3D;

        // 添加并刷新 Tile
        mapCoreScript.PushTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // 确保你有这个方法
    }

    #endregion
}
