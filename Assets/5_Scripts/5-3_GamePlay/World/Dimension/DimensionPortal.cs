using UnityEngine;

public sealed class DimensionPortal : MonoBehaviour, IInteractable, IItemPoolLifecycle
{
    #region 配置

    [SerializeField] private string targetDimensionId;
    [SerializeField] private bool requiresInstalledBuilding;

    public string TargetDimensionId => targetDimensionId;
    public bool RequiresInstalledBuilding => requiresInstalledBuilding;

    #endregion

    #region 运行时状态

    private Item portalItem;
    private Mod_Building building;
    private bool transitionRequested;
    private Vector2Int anchorCell;
    private bool initialized;
    private bool generatedWorldPortal;

    #endregion

    public void Initialize(string targetDimension)
    {
        targetDimensionId = targetDimension;
        anchorCell = GetCurrentCell();
        initialized = true;
        generatedWorldPortal = false;
        transitionRequested = false;
        portalItem = null;
        building = null;
    }

    private void OnEnable()
    {
        // Chunk 对象池会复用父物体；若入口随旧 Chunk 被搬到新坐标，立即清理。
        if (initialized && GetCurrentCell() != anchorCell)
            Destroy(gameObject);
    }

    public void Configure(string targetDimension, bool requireInstalledBuilding)
    {
        targetDimensionId = targetDimension;
        requiresInstalledBuilding = requireInstalledBuilding;
        generatedWorldPortal = false;
        initialized = false;
        transitionRequested = false;
        portalItem = null;
        building = null;
    }

    /// <summary>
    /// 由新版 ChunkView 为确定性自然入口调用。
    /// 该入口不依赖旧 Chunk/Map 锚点，另一维度使用同世界格的稳定出口。
    /// </summary>
    public void ConfigureGenerated(string targetDimension, Item ownerItem = null)
    {
        targetDimensionId = targetDimension;
        requiresInstalledBuilding = false;
        generatedWorldPortal = true;
        initialized = false;
        transitionRequested = false;
        portalItem = ownerItem;
        building = ownerItem?.GetComponentInChildren<Mod_Building>(true);
    }

    public void OnInteractStart(Item playerItem)
    {
        if (transitionRequested || playerItem is not Player player)
            return;

        CachePortalContext();
        if (string.IsNullOrWhiteSpace(targetDimensionId))
        {
            Debug.LogWarning("[DimensionPortal] 未配置目标维度。", this);
            return;
        }

        if (generatedWorldPortal)
        {
            transitionRequested = DimensionManager.Instance.TryBeginGeneratedPortalTransition(
                player, targetDimensionId, portalItem);
            return;
        }

        if (building != null && (building.IsSummoner || !building.IsInstalled()))
        {
            Debug.LogWarning("[DimensionPortal] 只有完成安装的矿坑建筑可以切换维度。", this);
            return;
        }

        if (requiresInstalledBuilding && building == null)
        {
            Debug.LogError("[DimensionPortal] 入口要求已安装建筑，但未找到 Mod_Building。", this);
            return;
        }

        transitionRequested = DimensionManager.Instance.TryBeginTransition(player, targetDimensionId, portalItem);
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    private void CachePortalContext()
    {
        portalItem ??= GetComponent<Item>();
        portalItem ??= GetComponentInParent<Item>();
        if (portalItem != null)
            building ??= portalItem.GetComponentInChildren<Mod_Building>(true);
    }

    private Vector2Int GetCurrentCell()
    {
        return new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));
    }

    #region 对象池生命周期

    /// <summary>从对象池取出时清理上一次维度入口留下的运行时状态。</summary>
    public void OnItemTakenFromPool()
    {
        ResetRuntimeState();
    }

    /// <summary>回收到对象池时清理锚点和交互缓存，避免下一次复用误删或误传送。</summary>
    public void OnItemReturnedToPool()
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        portalItem = null;
        building = null;
        transitionRequested = false;
        anchorCell = default;
        initialized = false;
        generatedWorldPortal = false;
    }

    #endregion
}
