using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 世界可见对象的排序接口组件。只持有主体 SpriteRenderer 和类别；静态层级由
/// WorldSortingManager 的类别 JSON 决定，动态对象沿用世界 Y 轴透明排序。
/// 主体以 Sprite Pivot 为脚点，附属特效继续按主体排序值计算自身偏移。
/// </summary>
public sealed class WorldSortingMember : MonoBehaviour
{
    #region 排序目标
    [SerializeField] private string category;
    [SerializeField] private SpriteRenderer targetRenderer;
    private Item owner;
    private SortingGroup targetGroup;
    private int originalLayerId;
    private int originalOrder;
    private SpriteSortPoint originalSortPoint;
    private bool hasOriginalSorting;

    /// <summary>配置类别。</summary>
    public string Category => category;

    /// <summary>主体精灵。</summary>
    public SpriteRenderer TargetRenderer => targetRenderer;

    /// <summary>手持物使用手持链自己的排序配置。</summary>
    public bool IsWorldVisible => owner == null || !owner.InHand;
    #endregion

    #region 注册与应用
    /// <summary>Prefab 或 MOD 显式声明的成员在激活时接入管理器。</summary>
    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(category) && IsWorldVisible)
        {
            targetGroup = ResolveSortingGroup(targetRenderer, owner);
            WorldSortingManager.GetInstance()?.Register(this);
        }
    }

    /// <summary>对象休眠时归还排序，重新激活后再按类别应用。</summary>
    private void OnDisable()
    {
        RestoreSorting();
    }

    /// <summary>实体注册后绑定主体；对象池复用时允许重新指定类别和渲染器。</summary>
    public void Bind(string sortingCategory, SpriteRenderer spriteRenderer, Item item = null)
    {
        if (owner != item)
        {
            if (owner != null) owner.OnInHandChanged -= HandleInHandChanged;
            owner = item;
            if (owner != null) owner.OnInHandChanged += HandleInHandChanged;
        }
        SortingGroup sortingGroup = ResolveSortingGroup(spriteRenderer, item);
        if (targetRenderer != spriteRenderer || targetGroup != sortingGroup)
        {
            RestoreSorting();
            hasOriginalSorting = false;
        }
        category = sortingCategory;
        targetRenderer = spriteRenderer;
        targetGroup = sortingGroup;
        if (IsWorldVisible) WorldSortingManager.GetInstance()?.Register(this);
        else RestoreSorting();
    }

    /// <summary>按配置设置静态排序键；动态对象只把 Pivot 作为 Y 轴脚点。</summary>
    public void Apply(int sortingLayerId, int sortingOrder, bool dynamicY)
    {
        if (targetRenderer == null)
        {
            Debug.LogError($"[WorldSorting] {name} 缺少主体 SpriteRenderer。", this);
            return;
        }

        if (!hasOriginalSorting)
        {
            originalLayerId = targetGroup != null ? targetGroup.sortingLayerID : targetRenderer.sortingLayerID;
            originalOrder = targetGroup != null ? targetGroup.sortingOrder : targetRenderer.sortingOrder;
            originalSortPoint = targetRenderer.spriteSortPoint;
            hasOriginalSorting = true;
        }
        if (targetGroup != null)
        {
            targetGroup.sortingLayerID = sortingLayerId;
            targetGroup.sortingOrder = sortingOrder;
        }
        else
        {
            targetRenderer.sortingLayerID = sortingLayerId;
            targetRenderer.sortingOrder = sortingOrder;
        }
        if (dynamicY && targetGroup == null)
            targetRenderer.spriteSortPoint = SpriteSortPoint.Pivot;
    }

    /// <summary>取实体内部最外层分组，让建筑整体参与 Y 轴排序。</summary>
    private static SortingGroup ResolveSortingGroup(SpriteRenderer renderer, Item item)
    {
        if (renderer == null) return null;
        SortingGroup selected = null;
        foreach (SortingGroup group in renderer.GetComponentsInParent<SortingGroup>(true))
        {
            if (!group.enabled) continue;
            if (item != null && group.transform != item.transform &&
                !group.transform.IsChildOf(item.transform)) continue;
            selected = group;
        }
        return selected;
    }

    /// <summary>进入手持链时交回原排序，落地后重新应用世界配置。</summary>
    private void HandleInHandChanged(bool inHand)
    {
        if (inHand) RestoreSorting();
        else WorldSortingManager.GetInstance()?.Register(this);
    }

    /// <summary>恢复绑定前的排序键。</summary>
    private void RestoreSorting()
    {
        if (!hasOriginalSorting || targetRenderer == null) return;
        if (targetGroup != null)
        {
            targetGroup.sortingLayerID = originalLayerId;
            targetGroup.sortingOrder = originalOrder;
        }
        else
        {
            targetRenderer.sortingLayerID = originalLayerId;
            targetRenderer.sortingOrder = originalOrder;
        }
        targetRenderer.spriteSortPoint = originalSortPoint;
    }

    /// <summary>断开实体事件。</summary>
    private void OnDestroy()
    {
        if (owner != null) owner.OnInHandChanged -= HandleInHandChanged;
    }
    #endregion
}
