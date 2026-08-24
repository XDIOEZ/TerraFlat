using UnityEngine;

/// <summary>
/// 手持食物的独立表现模块：食物每完成一口使用，就按照 Food.Max_EatingProgress
/// 与 EatingProgress 的比例缩小 SpriteMask 的可见区域；模块只在 Item.InHand 时
/// 创建遮罩，不改变营养结算、库存数量和食物本体数据。
/// </summary>
public sealed class Module_HeldFood : Module, IFoodMechanic, IFoodStateObserver
{
    public const string ModuleId = "手上食物模块";
    private const string HeldFoodSortingLayerName = "HeldFood";

    public enum CropDirection
    {
        RightToLeft,
        LeftToRight,
        TopToBottom,
        BottomToTop
    }

    #region 模块数据

    public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? new Ex_ModData_MemoryPackable();
    }

    #endregion

    #region 表现配置

    [Header("手持食物裁剪")]
    [Tooltip("食物从哪一侧逐步裁掉")]
    [SerializeField]
    private CropDirection cropDirection = CropDirection.RightToLeft;

    [Tooltip("遮罩边缘的额外像素单位，避免裁剪边缘出现缝隙")]
    [SerializeField, Min(0f)]
    private float maskPadding;

    #endregion

    #region 运行时状态

    private IFoodRuntimeContext foodContext;
    private SpriteRenderer targetRenderer;
    private Sprite cachedTargetSprite;
    private SpriteMask cropMask;
    private Sprite cropMaskSprite;
    private SpriteMaskInteraction originalMaskInteraction;
    private bool hasOriginalMaskInteraction;
    private int originalSortingLayerID;
    private int originalSortingOrder;
    private bool hasOriginalSorting;

    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public string MechanicId => "food.held.visual";
    public int Priority => 20;

    #endregion

    #region 生命周期

    public override void Load()
    {
        if (item == null)
            return;

        item.OnInHandChanged -= HandleInHandChanged;
        item.OnInHandChanged += HandleInHandChanged;
        RefreshMask();
    }

    public override void Unload()
    {
        if (item != null)
            item.OnInHandChanged -= HandleInHandChanged;

        ReleaseMask();
        foodContext = null;
    }

    /// <summary>手持食物表现不额外持久化数据，进食进度由 Mod_Food 统一保存。</summary>
    public override void Save()
    {
    }

    #endregion

    #region 食物状态观察

    /// <summary>由食物执行器绑定当前运行时上下文，并立即恢复保存的进食比例。</summary>
    public void BindFoodContext(IFoodRuntimeContext context)
    {
        foodContext = context;
        RefreshMask();
    }

    /// <summary>每次进食进度变化后刷新手持物的可见比例。</summary>
    public void OnFoodStateChanged(FoodStateChangedContext _)
    {
        RefreshMask();
    }

    private void HandleInHandChanged(bool inHand)
    {
        if (inHand)
            RefreshMask();
        else
            ReleaseMask();
    }

    #endregion

    #region 遮罩表现

    private void RefreshMask()
    {
        if (item == null || !item.InHand || foodContext == null)
        {
            ReleaseMask();
            return;
        }

        if (!EnsureMask())
            return;

        Food food = foodContext.Data;
        float maxBites = Mathf.Max(1f, food?.Max_EatingProgress ?? 1f);
        float eatenBites = Mathf.Clamp(foodContext.EatingProgress, 0f, maxBites);
        float visibleRatio = 1f - eatenBites / maxBites;
        ApplyVisibleRatio(visibleRatio);
    }

    private bool EnsureMask()
    {
        SpriteRenderer renderer = ResolveTargetRenderer();
        if (renderer == null || renderer.sprite == null)
            return false;

        if (cropMask != null && targetRenderer == renderer && cachedTargetSprite == renderer.sprite)
            return true;

        ReleaseMask();
        targetRenderer = renderer;
        cachedTargetSprite = renderer.sprite;
        originalMaskInteraction = renderer.maskInteraction;
        hasOriginalMaskInteraction = true;
        originalSortingLayerID = renderer.sortingLayerID;
        originalSortingOrder = renderer.sortingOrder;
        hasOriginalSorting = true;
        renderer.sortingLayerName = HeldFoodSortingLayerName;
        renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        GameObject maskObject = new GameObject("HeldFoodCropMask");
        maskObject.transform.SetParent(renderer.transform, false);
        cropMask = maskObject.AddComponent<SpriteMask>();
        cropMaskSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        cropMask.sprite = cropMaskSprite;
        cropMask.isCustomRangeActive = true;
        cropMask.frontSortingLayerID = renderer.sortingLayerID;
        cropMask.backSortingLayerID = renderer.sortingLayerID;
        cropMask.frontSortingOrder = renderer.sortingOrder + 1;
        cropMask.backSortingOrder = renderer.sortingOrder - 1;
        return true;
    }

    private void ApplyVisibleRatio(float visibleRatio)
    {
        if (cropMask == null || targetRenderer == null || targetRenderer.sprite == null)
            return;

        float ratio = Mathf.Clamp01(visibleRatio);
        if (ratio <= 0.0001f)
        {
            cropMask.enabled = false;
            return;
        }

        Bounds spriteBounds = targetRenderer.sprite.bounds;
        float width = Mathf.Max(0.0001f, spriteBounds.size.x);
        float height = Mathf.Max(0.0001f, spriteBounds.size.y);
        bool horizontal = cropDirection == CropDirection.RightToLeft ||
                          cropDirection == CropDirection.LeftToRight;
        float visibleSize = horizontal ? width * ratio : height * ratio;
        visibleSize = Mathf.Clamp(visibleSize + Mathf.Max(0f, maskPadding), 0.0001f,
            horizontal ? width : height);

        Vector3 localPosition = spriteBounds.center;
        Vector3 localScale = new Vector3(width, height, 1f);
        if (horizontal)
        {
            bool cropFromRight = cropDirection == CropDirection.RightToLeft;
            localPosition.x = cropFromRight
                ? spriteBounds.max.x - visibleSize * 0.5f
                : spriteBounds.min.x + visibleSize * 0.5f;
            localScale.x = visibleSize;
        }
        else
        {
            bool cropFromTop = cropDirection == CropDirection.TopToBottom;
            localPosition.y = cropFromTop
                ? spriteBounds.max.y - visibleSize * 0.5f
                : spriteBounds.min.y + visibleSize * 0.5f;
            localScale.y = visibleSize;
        }

        cropMask.enabled = true;
        Transform maskTransform = cropMask.transform;
        maskTransform.localPosition = localPosition;
        maskTransform.localRotation = Quaternion.identity;
        maskTransform.localScale = localScale;
    }

    private SpriteRenderer ResolveTargetRenderer()
    {
        if (targetRenderer != null && targetRenderer.sprite != null)
            return targetRenderer;

        SpriteRenderer itemRenderer = item.Sprite;
        if (itemRenderer != null && itemRenderer.sprite != null)
            return itemRenderer;

        SpriteRenderer[] renderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    private void ReleaseMask()
    {
        if (targetRenderer != null && hasOriginalMaskInteraction)
            targetRenderer.maskInteraction = originalMaskInteraction;
        if (targetRenderer != null && hasOriginalSorting)
        {
            targetRenderer.sortingLayerID = originalSortingLayerID;
            targetRenderer.sortingOrder = originalSortingOrder;
        }

        DestroyRuntimeObject(cropMask != null ? cropMask.gameObject : null);
        DestroyRuntimeObject(cropMaskSprite);

        targetRenderer = null;
        cachedTargetSprite = null;
        cropMask = null;
        cropMaskSprite = null;
        hasOriginalMaskInteraction = false;
        hasOriginalSorting = false;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    #endregion
}
