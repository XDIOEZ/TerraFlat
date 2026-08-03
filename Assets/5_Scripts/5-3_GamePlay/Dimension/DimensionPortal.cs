using UnityEngine;

public sealed class DimensionPortal : MonoBehaviour, IInteractable
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

    #endregion

    public void Initialize(string targetDimension)
    {
        targetDimensionId = targetDimension;
    }

    public void Configure(string targetDimension, bool requireInstalledBuilding)
    {
        targetDimensionId = targetDimension;
        requiresInstalledBuilding = requireInstalledBuilding;
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
}
