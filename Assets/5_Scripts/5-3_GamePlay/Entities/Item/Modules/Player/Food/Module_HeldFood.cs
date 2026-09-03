using UnityEngine;

/// <summary>
/// 手持食物的独立表现模块：每完成一口使用，就在食物原始外轮廓上确定性随机选点并裁出圆形咬痕。
/// 咬痕半径由 Food.Max_EatingProgress 决定，最后一口直接清空；模块不修改营养、库存或食物存档。
/// </summary>
public sealed class Module_HeldFood : Module, IFoodMechanic, IFoodStateObserver
{
    /// <summary>模块注册 ID。</summary>
    public const string ModuleId = "手上食物模块";

    /// <summary>确保运行时 SpriteMask 只影响手持食物。</summary>
    private const string HeldFoodSortingLayerName = "HeldFood";

    /// <summary>进食进度比较容差。</summary>
    private const float ProgressEpsilon = 0.0001f;

    #region 模块数据

    /// <summary>模块自身不保存表现数据，仅保留标准模块载体。</summary>
    public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();

    /// <summary>访问标准模块数据。</summary>
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? new Ex_ModData_MemoryPackable();
    }

    #endregion

    #region 运行时状态

    /// <summary>食用次数和食物配置的权威运行时上下文。</summary>
    private IFoodRuntimeContext foodContext;

    /// <summary>当前被遮罩的手持食物渲染器。</summary>
    private SpriteRenderer targetRenderer;

    /// <summary>当前被遮罩的食物 Sprite。</summary>
    private Sprite cachedTargetSprite;

    /// <summary>限制手持食物可见区域的运行时遮罩。</summary>
    private SpriteMask biteMask;

    /// <summary>承载圆形咬痕像素的运行时纹理。</summary>
    private Texture2D biteMaskTexture;

    /// <summary>供 SpriteMask 使用的运行时 Sprite。</summary>
    private Sprite biteMaskSprite;

    /// <summary>当前食物外形对应的咬痕几何生成器。</summary>
    private FoodBiteMaskGenerator biteMaskGenerator;

    /// <summary>遮罩接管前的 SpriteMask 交互方式。</summary>
    private SpriteMaskInteraction originalMaskInteraction;

    /// <summary>是否已记录原 SpriteMask 交互方式。</summary>
    private bool hasOriginalMaskInteraction;

    /// <summary>遮罩接管前的 Sorting Layer。</summary>
    private int originalSortingLayerID;

    /// <summary>遮罩接管前的排序值。</summary>
    private int originalSortingOrder;

    /// <summary>是否已记录原排序配置。</summary>
    private bool hasOriginalSorting;

    /// <summary>已经提交到纹理的食用口数。</summary>
    private int appliedBiteCount = -1;

    /// <summary>已经提交到纹理的最大口数。</summary>
    private int appliedMaximumBiteCount = -1;

    /// <summary>已经提交到纹理的确定性随机种子。</summary>
    private int appliedRandomSeed;

    /// <summary>表现模块不参与 Tick。</summary>
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    /// <summary>食物规则管线中的稳定机制 ID。</summary>
    public string MechanicId => "food.held.visual";

    /// <summary>在通用反馈后刷新手持食物表现。</summary>
    public int Priority => 20;

    #endregion

    #region 生命周期

    /// <summary>绑定手持状态变化并恢复当前食用进度的遮罩。</summary>
    public override void Load()
    {
        if (item == null)
            return;

        item.OnInHandChanged -= HandleInHandChanged;
        item.OnInHandChanged += HandleInHandChanged;
        RefreshMask();
    }

    /// <summary>解绑手持状态并释放所有运行时遮罩对象。</summary>
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

    /// <summary>绑定当前食物运行时上下文并恢复已完成的咬痕。</summary>
    public void BindFoodContext(IFoodRuntimeContext context)
    {
        foodContext = context;
        RefreshMask();
    }

    /// <summary>进食进度变化后刷新圆形咬痕。</summary>
    public void OnFoodStateChanged(FoodStateChangedContext _)
    {
        RefreshMask();
    }

    /// <summary>只在物品位于手上时保留遮罩对象。</summary>
    private void HandleInHandChanged(bool inHand)
    {
        if (inHand)
            RefreshMask();
        else
            ReleaseMask();
    }

    #endregion

    #region 遮罩表现

    /// <summary>把当前食用进度转换为沿食物外轮廓随机分布的圆形咬痕遮罩。</summary>
    private void RefreshMask()
    {
        if (item == null || !item.InHand || foodContext == null)
        {
            ReleaseMask();
            return;
        }

        Food food = foodContext.Data;
        float maximumProgress = Mathf.Max(ProgressEpsilon, food?.Max_EatingProgress ?? 1f);
        int maximumBites = Mathf.Max(1, Mathf.CeilToInt(maximumProgress));
        if (!EnsureMask(maximumBites))
            return;

        float eatingProgress = Mathf.Max(0f, foodContext.EatingProgress);
        bool isFinalBite = eatingProgress >= maximumProgress - ProgressEpsilon;
        int completedBites = isFinalBite
            ? maximumBites
            : Mathf.Clamp(Mathf.FloorToInt(eatingProgress + ProgressEpsilon), 0, maximumBites - 1);
        int randomSeed = CreateBiteSeed(maximumBites);
        ApplyBiteMask(completedBites, maximumBites, randomSeed);
    }

    /// <summary>为当前食物 Sprite 创建或复用运行时圆形遮罩。</summary>
    private bool EnsureMask(int maximumBites)
    {
        SpriteRenderer renderer = ResolveTargetRenderer();
        if (renderer == null || renderer.sprite == null)
            return false;

        if (biteMask != null &&
            biteMaskGenerator != null &&
            targetRenderer == renderer &&
            cachedTargetSprite == renderer.sprite &&
            appliedMaximumBiteCount == maximumBites)
        {
            return true;
        }

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

        biteMaskGenerator = new FoodBiteMaskGenerator(renderer.sprite, maximumBites);
        CreateMaskTexture();
        CreateMaskObject(renderer);
        appliedBiteCount = -1;
        appliedMaximumBiteCount = maximumBites;
        return true;
    }

    /// <summary>创建承载咬痕像素的运行时纹理和 Sprite。</summary>
    private void CreateMaskTexture()
    {
        biteMaskTexture = new Texture2D(
            biteMaskGenerator.Width,
            biteMaskGenerator.Height,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "HeldFoodBiteMaskTexture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        biteMaskGenerator.Build(0, 0);
        biteMaskTexture.SetPixels32(biteMaskGenerator.MaskPixels);
        biteMaskTexture.Apply(false, false);

        biteMaskSprite = Sprite.Create(
            biteMaskTexture,
            new Rect(0f, 0f, biteMaskTexture.width, biteMaskTexture.height),
            new Vector2(0.5f, 0.5f),
            1f,
            0,
            SpriteMeshType.FullRect);
        biteMaskSprite.name = "HeldFoodBiteMaskSprite";
    }

    /// <summary>创建 SpriteMask 并对齐目标食物的本地边界和排序范围。</summary>
    private void CreateMaskObject(SpriteRenderer renderer)
    {
        GameObject maskObject = new GameObject("HeldFoodBiteMask");
        maskObject.transform.SetParent(renderer.transform, false);
        biteMask = maskObject.AddComponent<SpriteMask>();
        biteMask.sprite = biteMaskSprite;
        biteMask.alphaCutoff = 0.5f;
        biteMask.isCustomRangeActive = true;
        biteMask.frontSortingLayerID = renderer.sortingLayerID;
        biteMask.backSortingLayerID = renderer.sortingLayerID;
        biteMask.frontSortingOrder = renderer.sortingOrder + 1;
        biteMask.backSortingOrder = renderer.sortingOrder - 1;

        Bounds spriteBounds = biteMaskGenerator.SpriteBounds;
        Transform maskTransform = biteMask.transform;
        maskTransform.localPosition = spriteBounds.center;
        maskTransform.localRotation = Quaternion.identity;
        maskTransform.localScale = new Vector3(
            spriteBounds.size.x / biteMaskSprite.bounds.size.x,
            spriteBounds.size.y / biteMaskSprite.bounds.size.y,
            1f);
    }

    /// <summary>仅在口数或随机序列变化时更新遮罩纹理。</summary>
    private void ApplyBiteMask(int completedBites, int maximumBites, int randomSeed)
    {
        if (biteMask == null || biteMaskTexture == null || biteMaskGenerator == null)
            return;
        if (appliedBiteCount == completedBites &&
            appliedMaximumBiteCount == maximumBites &&
            appliedRandomSeed == randomSeed)
        {
            return;
        }

        bool hasVisibleFood = biteMaskGenerator.Build(completedBites, randomSeed);
        biteMaskTexture.SetPixels32(biteMaskGenerator.MaskPixels);
        biteMaskTexture.Apply(false, false);
        biteMask.enabled = hasVisibleFood;
        appliedBiteCount = completedBites;
        appliedMaximumBiteCount = maximumBites;
        appliedRandomSeed = randomSeed;
    }

    /// <summary>读取食物本体 SpriteRenderer，忽略没有 Sprite 的模块子对象。</summary>
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

    /// <summary>
    /// 由物品 GUID、定义 ID、Sprite 和当前堆叠数量生成跨重新持有稳定的随机种子。
    /// 每吃完一整份后堆叠数量会变化，因此下一份食物会从不同的外轮廓位置开始产生咬痕。
    /// </summary>
    private int CreateBiteSeed(int maximumBites)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (item?.itemData?.Guid ?? 0);
            hash = hash * 31 + GetStableStringHash(item?.itemData?.IDName);
            hash = hash * 31 + GetStableStringHash(cachedTargetSprite?.name);
            hash = hash * 31 + Mathf.RoundToInt(item?.itemData?.Stack?.Amount ?? 0f);
            hash = hash * 31 + maximumBites;
            return hash;
        }
    }

    /// <summary>计算不依赖运行时进程盐值的稳定字符串哈希。</summary>
    private static int GetStableStringHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
            }

            return (int)hash;
        }
    }

    /// <summary>恢复渲染器原状态并销毁运行时遮罩资源。</summary>
    private void ReleaseMask()
    {
        if (targetRenderer != null && hasOriginalMaskInteraction)
            targetRenderer.maskInteraction = originalMaskInteraction;
        if (targetRenderer != null && hasOriginalSorting)
        {
            targetRenderer.sortingLayerID = originalSortingLayerID;
            targetRenderer.sortingOrder = originalSortingOrder;
        }
        if (biteMask != null)
            biteMask.enabled = false;

        DestroyRuntimeObject(biteMask != null ? biteMask.gameObject : null);
        DestroyRuntimeObject(biteMaskSprite);
        DestroyRuntimeObject(biteMaskTexture);

        targetRenderer = null;
        cachedTargetSprite = null;
        biteMask = null;
        biteMaskTexture = null;
        biteMaskSprite = null;
        biteMaskGenerator = null;
        hasOriginalMaskInteraction = false;
        hasOriginalSorting = false;
        appliedBiteCount = -1;
        appliedMaximumBiteCount = -1;
        appliedRandomSeed = 0;
    }

    /// <summary>按运行模式安全销毁临时 Unity 对象。</summary>
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
