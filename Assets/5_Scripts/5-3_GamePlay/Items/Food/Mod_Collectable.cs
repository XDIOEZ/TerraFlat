using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 通用采集模块：保存资源库存、接收生产模块产出，并把一次交互转换成一个世界掉落物。
/// 资源种类、容量、自然初始库存和成熟提示均由物品 JSON 配置，不包含浆果丛等具体物品规则。
/// </summary>
public sealed class Mod_Collectable : Module, IInteractable, IItemPoolLifecycle,
    INaturalResourceInitializer, IProductionStockReceiver, ICropHarvestAction,
    ICropHarvestPolicy, IItemModuleDependencyBinder
{
    #region 模块数据

    private const string ModuleId = "采集模块";

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    /// <summary>可采集库存的持久化数据。</summary>
    public CollectableModuleData Data = new CollectableModuleData();

    public override ModuleData _Data
    {
        get => Data;
        set => Data = value as CollectableModuleData ??
            throw new ArgumentException("[Mod_Collectable] 模块数据类型错误。", nameof(value));
    }

    #endregion

    #region 配置

    [Header("采集配置")]
    public string CollectItemId = "Berry";

    [Min(1)] public int MaxStock = 12;
    [Min(0)] public int NaturalInitialStockMin = 1;
    [Min(0)] public int NaturalInitialStockMax = 2;

    [Min(0.05f)] public float SpawnRadius = 1.2f;
    [Min(0.05f)] public float ThrowDuration = 0.5f;
    public float ThrowBezierOffset = 0.8f;
    public float ThrowArcHeight = 0.6f;

    [Header("库存提示")]
    public List<SpriteRenderer> IndicatorRenderers = new List<SpriteRenderer>();
    public List<Vector3> IndicatorLocalPositions = new List<Vector3>
    {
        new Vector3(0.25600004f, 0.549f, 0f),
        new Vector3(-0.11899996f, 0.7f, 0f),
        new Vector3(-0.28900003f, 0.389f, 0f)
    };
    [Min(0.01f)] public float IndicatorScale = 0.2859f;
    public Sprite IndicatorSpriteOverride;

    [Header("库存提示图层")]
    public bool FollowOwnerRendererSorting = true;
    public int IndicatorSortingOrderOffset = 1;
    public string IndicatorSortingLayerName = "Default";
    public int IndicatorSortingOrder;

    #endregion

    #region 运行时

    /// <summary>按物品 ID 缓存采集物的运行时视觉定义。</summary>
    private readonly Dictionary<string, RuntimeItemDefinition> itemDefinitionCache =
        new Dictionary<string, RuntimeItemDefinition>(StringComparer.OrdinalIgnoreCase);
    private SpriteRenderer ownerRenderer;
    private Mod_Crop cropModule;

    public int CurrentStock => Data?.CurrentStock ?? 0;
    public bool CanHarvest
    {
        get
        {
            EnsureMatureStockInitialized();
            return Data != null && Data.CurrentStock > 0 && IsCropMatureOrStandalone();
        }
    }
    public bool PreserveCropAfterHarvest => true;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        EnsureDataContainer();
        base.Awake();
        EnsureIndicatorRenderers();
    }

    public override void Load()
    {
        EnsureConfiguration();
        EnsureDataContainer();
        Data.CurrentStock = Mathf.Clamp(Data.CurrentStock, 0, Mathf.Max(1, MaxStock));

        if (cropModule != null)
        {
            cropModule.Matured -= HandleCropMatured;
            cropModule.Matured += HandleCropMatured;
            if (cropModule.Stage == CropStage.Mature && !Data.IsInitialized)
                InitializeNaturalResource(unchecked((uint)item.itemData.Guid));
        }

        RefreshIndicatorPresentation();
    }

    public override void Save()
    {
        EnsureDataContainer();
        Data.CurrentStock = Mathf.Clamp(Data.CurrentStock, 0, Mathf.Max(1, MaxStock));
    }

    public override void Unload()
    {
        if (cropModule != null)
            cropModule.Matured -= HandleCropMatured;
        cropModule = null;
    }

    /// <summary>显式解析可选的作物成熟状态；同一实体最多只能有一个作物状态模块。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        cropModule = null;
        foreach (Module module in modules.Mods.Values)
        {
            if (module is not Mod_Crop crop)
                continue;
            if (cropModule != null)
                throw new InvalidOperationException("[Mod_Collectable] 同一物品存在多个 Mod_Crop，无法确定采集成熟状态。");
            cropModule = crop;
        }
    }

    #endregion

    #region 生产与自然初始化

    /// <summary>声明本模块负责接收的物品类型。</summary>
    public bool AcceptsProduction(string itemId)
    {
        return string.Equals(itemId?.Trim(), CollectItemId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>仅在库存未满时允许生产模块继续推进。</summary>
    public bool CanAcceptProduction(string itemId)
    {
        EnsureMatureStockInitialized();
        return AcceptsProduction(itemId) && Data != null &&
               IsCropMatureOrStandalone() &&
               Data.CurrentStock < Mathf.Max(1, MaxStock);
    }

    /// <summary>把生产结果写入采集库存，超过容量的部分不进入库存。</summary>
    public int AcceptProduction(string itemId, int amount)
    {
        if (!CanAcceptProduction(itemId) || amount <= 0)
            return 0;

        int accepted = Mathf.Min(amount, Mathf.Max(1, MaxStock) - Data.CurrentStock);
        Data.CurrentStock += accepted;
        Data.IsInitialized = true;
        RefreshIndicatorPresentation();
        return accepted;
    }

    /// <summary>使用自然物确定性 GUID 初始化库存，已有权威状态时不重复覆盖。</summary>
    public void InitializeNaturalResource(uint deterministicRandomValue)
    {
        EnsureDataContainer();
        if (Data.IsInitialized)
        {
            RefreshIndicatorPresentation();
            return;
        }

        int capacity = Mathf.Max(1, MaxStock);
        int minCount = Mathf.Clamp(NaturalInitialStockMin, 0, capacity);
        int maxCount = Mathf.Clamp(NaturalInitialStockMax, minCount, capacity);
        uint range = (uint)(maxCount - minCount + 1);

        Data.CurrentStock = minCount + (int)(deterministicRandomValue % range);
        Data.IsInitialized = true;
        RefreshIndicatorPresentation();
    }

    #endregion

    #region 交互采集

    public bool CanInteract(Item playerItem)
    {
        // 挂在作物上时由 Mod_Crop 统一仲裁交互，避免同一次输入重复采摘。
        return cropModule == null && playerItem != null && CanHarvest;
    }

    public void OnInteractStart(Item playerItem)
    {
        if (playerItem == null)
            throw new ArgumentNullException(nameof(playerItem));
        if (cropModule != null || !CanHarvest)
            return;

        SpawnCollectedItem();
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    /// <summary>作为持续采果作物的收获动作，每次只扣一份库存且不销毁植株。</summary>
    public void Execute(CropHarvestContext context)
    {
        if (context.CropItem != item)
            throw new InvalidOperationException("[Mod_Collectable] 收获上下文与模块所属 Item 不一致。");
        if (!CanHarvest)
            throw new InvalidOperationException($"[Mod_Collectable] 作物 {item.itemData?.IDName} 当前没有可采集库存。");

        SpawnCollectedItem();
    }

    /// <summary>生成一次采集掉落，并扣除一份库存。</summary>
    private void SpawnCollectedItem()
    {
        Vector2 startPosition = ResolveHarvestStartPosition();
        GameObject parent = item.transform.parent != null ? item.transform.parent.gameObject : null;
        Item collectedItem = ItemMgr.Instance.InstantiateItem(
            CollectItemId,
            startPosition,
            Quaternion.identity,
            Vector3.one,
            parent);
        if (collectedItem == null)
            throw new MissingReferenceException(
                $"[Mod_Collectable] 采集物实例化失败，物品ID={CollectItemId}");

        collectedItem.Load();
        collectedItem.SetInHand(false);

        // 一次交互严格对应一份库存和一个采集物；难度掉落倍率不放大单次采摘数量。
        const int outputAmount = 1;
        collectedItem.itemData.Stack.Amount = outputAmount;
        Data.CurrentStock--;
        Data.IsInitialized = true;

        ApplyParabolaThrow(collectedItem, startPosition);
        GetComponentInParent<ChunkNaturalItemRenderer>(true)?.RegisterTransientItem(collectedItem);

        RefreshIndicatorVisual();
    }

    /// <summary>通过统一掉落模块播放采集抛物线。</summary>
    private void ApplyParabolaThrow(Item collectedItem, Vector2 startPosition)
    {
        Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
        float distance = UnityEngine.Random.Range(0.5f * SpawnRadius, SpawnRadius);
        Vector2 endPosition = startPosition + direction * distance;

        Mod_BaseDroper.StaticDropItem_Pos(
            collectedItem,
            startPosition,
            endPosition,
            Mathf.Max(0.05f, ThrowDuration),
            Mod_BaseDroper.MoveMode.BezierCurve,
            ThrowBezierOffset,
            ThrowArcHeight);
    }

    #endregion

    #region 库存提示

    /// <summary>根据当前库存完整重建并刷新可采集物提示。</summary>
    private void RefreshIndicatorPresentation()
    {
        ResolveOwnerRenderer();
        EnsureIndicatorRenderers();
        ApplyIndicatorLayout();
        ApplyIndicatorAppearance();
        ApplyIndicatorSorting();
        RefreshIndicatorVisual();
    }

    private void ResolveOwnerRenderer()
    {
        Item ownerItem = item != null ? item : GetComponentInParent<Item>();
        ownerRenderer = ownerItem?.Sprite;
        if (ownerRenderer == null && ownerItem != null)
        {
            SpriteRenderer[] renderers = ownerItem.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer != null && !renderer.transform.IsChildOf(transform))
                {
                    ownerRenderer = renderer;
                    break;
                }
            }
        }

        SyncOwnerSortingGroup(ownerItem);
    }

    /// <summary>把主体与提示图标作为一个整体参与世界 Y 排序，避免提示的内部高层级压过角色。</summary>
    private void SyncOwnerSortingGroup(Item ownerItem)
    {
        if (!FollowOwnerRendererSorting || ownerItem == null || ownerRenderer == null)
            return;

        SortingGroup sortingGroup = ownerItem.GetComponent<SortingGroup>();
        if (sortingGroup == null)
        {
            throw new MissingComponentException(
                $"[Mod_Collectable] 物品 {ownerItem.name} 的外壳缺少 SortingGroup，无法把主体与采集提示作为同一深度单元排序。");
        }

        sortingGroup.enabled = true;
        sortingGroup.sortingLayerID = ownerRenderer.sortingLayerID;
        sortingGroup.sortingOrder = ownerRenderer.sortingOrder;
    }

    private void EnsureIndicatorRenderers()
    {
        IndicatorRenderers ??= new List<SpriteRenderer>();
        IndicatorLocalPositions ??= new List<Vector3>();
        float scale = Mathf.Max(0.01f, IndicatorScale);
        for (int i = 0; i < IndicatorLocalPositions.Count; i++)
        {
            if (i < IndicatorRenderers.Count && IndicatorRenderers[i] != null)
                continue;

            GameObject marker = new GameObject($"CollectIndicator_{i + 1}");
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = IndicatorLocalPositions[i];
            marker.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
            if (i < IndicatorRenderers.Count)
                IndicatorRenderers[i] = renderer;
            else
                IndicatorRenderers.Add(renderer);
        }
    }

    private void ApplyIndicatorLayout()
    {
        float scale = Mathf.Max(0.01f, IndicatorScale);
        for (int i = 0; i < IndicatorRenderers.Count; i++)
        {
            SpriteRenderer renderer = IndicatorRenderers[i];
            if (renderer == null)
                continue;

            bool used = i < IndicatorLocalPositions.Count;
            renderer.gameObject.SetActive(used);
            if (!used)
                continue;

            renderer.transform.localPosition = IndicatorLocalPositions[i];
            renderer.transform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>让库存提示复用采集物的权威 Sprite 与受光材质。</summary>
    private void ApplyIndicatorAppearance()
    {
        RuntimeItemDefinition definition = ResolveRuntimeItemDefinition(CollectItemId);
        Sprite sprite = IndicatorSpriteOverride != null
            ? IndicatorSpriteOverride
            : definition.Sprite;
        if (sprite == null)
            throw new MissingReferenceException(
                $"[Mod_Collectable] 找不到采集物图标，物品ID={CollectItemId}");
        if (definition.Material == null)
            throw new MissingReferenceException(
                $"[Mod_Collectable] 找不到采集物材质，物品ID={CollectItemId}");

        foreach (SpriteRenderer renderer in IndicatorRenderers)
        {
            if (renderer == null)
                continue;

            renderer.sprite = sprite;
            renderer.sharedMaterial = definition.Material;
        }
    }

    private void ApplyIndicatorSorting()
    {
        if (FollowOwnerRendererSorting && ownerRenderer != null)
            SyncOwnerSortingGroup(item != null ? item : GetComponentInParent<Item>());

        foreach (SpriteRenderer renderer in IndicatorRenderers)
        {
            if (renderer == null)
                continue;

            if (FollowOwnerRendererSorting && ownerRenderer != null)
            {
                renderer.sortingLayerID = ownerRenderer.sortingLayerID;
                renderer.sortingOrder = ownerRenderer.sortingOrder + IndicatorSortingOrderOffset;
            }
            else
            {
                renderer.sortingLayerName = IndicatorSortingLayerName;
                renderer.sortingOrder = IndicatorSortingOrder;
            }
        }
    }

    private void RefreshIndicatorVisual()
    {
        int visibleCount = IsCropMatureOrStandalone()
            ? Mathf.Min(CurrentStock, IndicatorRenderers?.Count ?? 0)
            : 0;
        if (IndicatorRenderers == null)
            return;

        for (int i = 0; i < IndicatorRenderers.Count; i++)
        {
            if (IndicatorRenderers[i] != null)
                IndicatorRenderers[i].enabled = i < visibleCount;
        }
    }

    private Vector2 ResolveHarvestStartPosition()
    {
        int count = IndicatorRenderers?.Count ?? 0;
        if (count == 0)
            return transform.position;

        int logicalIndex = CurrentStock > count
            ? (CurrentStock - 1) % count
            : Mathf.Clamp(CurrentStock - 1, 0, count - 1);
        for (int step = 0; step < count; step++)
        {
            SpriteRenderer renderer = IndicatorRenderers[(logicalIndex - step + count) % count];
            if (renderer != null && renderer.sprite != null)
                return renderer.transform.position;
        }

        return transform.position;
    }

    /// <summary>从运行时目录解析并缓存采集物定义。</summary>
    private RuntimeItemDefinition ResolveRuntimeItemDefinition(string itemId)
    {
        if (itemDefinitionCache.TryGetValue(itemId, out RuntimeItemDefinition cached))
            return cached;

        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
        {
            throw new MissingReferenceException(
                $"[Mod_Collectable] 找不到采集物定义，物品ID={itemId}");
        }

        itemDefinitionCache[itemId] = definition;
        return definition;
    }

    #endregion

    #region 对象池

    public void OnItemTakenFromPool()
    {
        ResetPoolState();
    }

    public void OnItemReturnedToPool()
    {
        ResetPoolState();
    }

    private void ResetPoolState()
    {
        Data = new CollectableModuleData { ID = ModuleId };
        cropModule = null;
        RefreshIndicatorVisual();
    }

    /// <summary>植株第一次成熟时建立首批果实，之后由生产模块周期补充库存。</summary>
    private void HandleCropMatured(Mod_Crop source)
    {
        if (source != cropModule)
            return;

        EnsureMatureStockInitialized();
        RefreshIndicatorPresentation();
    }

    private void EnsureMatureStockInitialized()
    {
        if (cropModule == null || cropModule.Stage != CropStage.Mature ||
            Data == null || Data.IsInitialized || item?.itemData == null)
            return;

        InitializeNaturalResource(unchecked((uint)item.itemData.Guid));
    }

    private bool IsCropMatureOrStandalone()
    {
        return cropModule == null || cropModule.Stage == CropStage.Mature;
    }

    private void EnsureDataContainer()
    {
        Data ??= new CollectableModuleData();
        Data.ID = ModuleId;
    }

    private void EnsureConfiguration()
    {
        if (string.IsNullOrWhiteSpace(CollectItemId))
            throw new InvalidOperationException("[Mod_Collectable] CollectItemId 不能为空。");
        if (IndicatorLocalPositions == null || IndicatorLocalPositions.Count == 0)
            throw new InvalidOperationException("[Mod_Collectable] 必须配置至少一个库存提示位置。");
    }

    #endregion

    #region 编辑器

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.2f, 0.9f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, SpawnRadius);
    }

    #endregion
}
