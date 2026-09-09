using System;
using UnityEngine;

/// <summary>
/// 作物表现模块：把作物精灵裁成半埋状态，并把 Mod_Crop 的连续进度映射为离散阶段 Sprite 或缩放。
/// visual.spriteStates 配齐 seedling/growing/mature 时优先切换三阶段图片；未配置时保留旧的连续缩放表现。
/// 不参与种植、成长结算或收获交互，对象卸载时恢复外壳原始材质、Sprite 与缩放。
/// </summary>
public sealed class Mod_CropVisual : Module
{
    #region Shader 属性

    private static readonly int BodyClipProperty = Shader.PropertyToID("_BodyClip");
    private static readonly int BodyMinVProperty = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxVProperty = Shader.PropertyToID("_BodyMaxV");

    #endregion

    #region 模块数据

    public Ex_ModData CropVisualData = new();

    public override ModuleData _Data
    {
        get => CropVisualData;
        set => CropVisualData = value as Ex_ModData ??
            throw new ArgumentException("[Mod_CropVisual] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => ModText.CropVisual;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 配置

    [Header("地表遮挡")]
    [Range(0f, 1f)]
    [Tooltip("裁掉精灵下方的比例；0.5 表示根部约一半埋入土地。")]
    public float buriedClip = 0.5f;

    [Header("地表遮挡材质")]
    [SerializeField]
    private Material buriedMaterial;

    [Header("成长缩放")]
    [SerializeField, Range(0.01f, 1f)]
    private float seedlingScale = 0.25f;

    [SerializeField, Min(0.01f)]
    private float matureScale = 1f;

    [Header("阶段图切换")]
    [SerializeField, Range(0.01f, 0.99f)]
    [Tooltip("配置三阶段 Sprite 时，成长进度达到该值后从幼苗图切换为生长期图；成熟图固定在 100% 时启用。")]
    private float growingVisualThreshold = 0.34f;

    #endregion

    #region 运行时

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Material originalMaterial;
    private bool materialOverridden;
    private Mod_Crop cropModule;
    private Vector3 originalLocalScale;
    private bool scaleCaptured;
    private Color originalColor; // 受害颜色叠加前的定义颜色。
    private Sprite originalSprite;
    private Sprite seedlingSprite;
    private Sprite growingSprite;
    private Sprite matureSprite;
    private bool useDiscreteGrowthSprites;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        base.Awake();
        CropVisualData ??= new Ex_ModData();
        CropVisualData.ID = ModText.CropVisual;
    }

    public override void Load()
    {
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_CropVisual] 未找到所属 Item。");

        spriteRenderer = item.Sprite ?? item.GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            throw new MissingComponentException("[Mod_CropVisual] 作物缺少有效 SpriteRenderer。");

        if (buriedMaterial == null)
            throw new MissingReferenceException("[Mod_CropVisual] 未配置支持 BodyClip 的 Sprite-Lit-Master 材质。");

        cropModule = item.GetComponentInChildren<Mod_Crop>(true);
        if (cropModule == null)
            throw new MissingComponentException("[Mod_CropVisual] 所属作物缺少 Mod_Crop。");

        originalLocalScale = spriteRenderer.transform.localScale;
        originalColor = spriteRenderer.color;
        scaleCaptured = true;
        originalSprite = spriteRenderer.sprite;
        ResolveGrowthSprites();
        ApplyBuriedMaterial();
        cropModule.GrowthChanged += HandleGrowthChanged;
        ApplyGrowthVisual(cropModule.NormalizedGrowth);
    }

    public override void Save()
    {
        // 表现参数来自 ItemDefinition，运行时只写入材质属性块，不产生独立状态。
    }

    public override void Unload()
    {
        UnbindCrop();
        ClearVisualState();
        RestoreOriginalScale();
        RestoreOriginalSprite();
        RestoreOriginalMaterial();
        spriteRenderer = null;
        propertyBlock = null;
    }

    private void OnDisable()
    {
        UnbindCrop();
        ClearVisualState();
        RestoreOriginalScale();
        RestoreOriginalSprite();
        RestoreOriginalMaterial();
    }

    private void OnDestroy()
    {
        Unload();
    }

    #endregion

    #region 成长表现

    private void HandleGrowthChanged(Mod_Crop source, float normalizedGrowth)
    {
        if (source == cropModule)
            ApplyGrowthVisual(normalizedGrowth);
    }

    /// <summary>优先使用三阶段 Sprite；没有阶段图时再使用旧的连续缩放。</summary>
    private void ApplyGrowthVisual(float normalizedGrowth)
    {
        if (!useDiscreteGrowthSprites)
        {
            ApplyGrowthScale(normalizedGrowth);
            ApplyClimateStressTint();
            ApplyVisualState();
            return;
        }

        float growth = Mathf.Clamp01(normalizedGrowth);
        Sprite target = growth >= 1f
            ? matureSprite
            : growth >= growingVisualThreshold
                ? growingSprite
                : seedlingSprite;
        if (spriteRenderer.sprite != target)
            spriteRenderer.sprite = target;

        if (scaleCaptured)
            spriteRenderer.transform.localScale = originalLocalScale;
        ApplyClimateStressTint();
        ApplyVisualState();
    }

    /// <summary>从运行时物品定义解析三阶段 Sprite；只要声明其中一张，就要求三张全部存在。</summary>
    private void ResolveGrowthSprites()
    {
        useDiscreteGrowthSprites = false;
        seedlingSprite = null;
        growingSprite = null;
        matureSprite = null;

        if (GameRes.Instance == null || item?.itemData == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition))
        {
            return;
        }

        bool hasSeedling = definition.TryGetVisualStateSprite("seedling", out seedlingSprite);
        bool hasGrowing = definition.TryGetVisualStateSprite("growing", out growingSprite);
        bool hasMature = definition.TryGetVisualStateSprite("mature", out matureSprite);
        if (!hasSeedling && !hasGrowing && !hasMature)
            return;
        if (!hasSeedling || !hasGrowing || !hasMature)
            throw new InvalidOperationException(
                $"[Mod_CropVisual] 作物 {item.itemData.IDName} 的 visual.spriteStates 必须同时配置 seedling/growing/mature。");

        useDiscreteGrowthSprites = true;
    }

    /// <summary>在幼苗与成熟缩放之间连续插值，作为没有阶段图的兼容表现。</summary>
    private void ApplyGrowthScale(float normalizedGrowth)
    {
        if (!scaleCaptured || spriteRenderer == null)
            return;

        float scale = Mathf.Lerp(seedlingScale, matureScale, Mathf.Clamp01(normalizedGrowth));
        spriteRenderer.transform.localScale = new Vector3(
            originalLocalScale.x * scale,
            originalLocalScale.y * scale,
            originalLocalScale.z);
    }

    /// <summary>叠加气候受害颜色，离散成长图与连续缩放表现共用同一规则。</summary>
    private void ApplyClimateStressTint()
    {
        if (spriteRenderer == null || cropModule == null)
            return;

        spriteRenderer.color = Color.Lerp(
            originalColor,
            new Color(0.58f, 0.42f, 0.20f, originalColor.a),
            cropModule.ClimateStress);
    }

    private void UnbindCrop()
    {
        if (cropModule != null)
            cropModule.GrowthChanged -= HandleGrowthChanged;
        cropModule = null;
    }

    /// <summary>避免对象池复用时残留上一株作物的成长缩放。</summary>
    private void RestoreOriginalScale()
    {
        if (!scaleCaptured || spriteRenderer == null)
            return;

        spriteRenderer.transform.localScale = originalLocalScale;
        spriteRenderer.color = originalColor;
        originalLocalScale = Vector3.one;
        scaleCaptured = false;
    }

    /// <summary>避免共享 CropShell 被对象池复用后残留上一种作物的阶段图。</summary>
    private void RestoreOriginalSprite()
    {
        if (spriteRenderer != null && originalSprite != null)
            spriteRenderer.sprite = originalSprite;
        originalSprite = null;
        seedlingSprite = null;
        growingSprite = null;
        matureSprite = null;
        useDiscreteGrowthSprites = false;
    }

    #endregion

    #region 半埋表现

    /// <summary>使用材质属性裁掉精灵下半部分，让根部藏入耕地。</summary>
    private void ApplyVisualState()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
        Bounds spriteBounds = spriteRenderer.sprite.bounds;
        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(BodyMinVProperty, spriteBounds.min.y);
        propertyBlock.SetFloat(BodyMaxVProperty, spriteBounds.max.y);
        propertyBlock.SetFloat(BodyClipProperty, Mathf.Clamp01(buriedClip));
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>为作物绑定支持 BodyClip 的专用材质，不修改原始 Sprite 资源。</summary>
    private void ApplyBuriedMaterial()
    {
        if (spriteRenderer.sharedMaterial == buriedMaterial)
            return;

        originalMaterial = spriteRenderer.sharedMaterial;
        spriteRenderer.sharedMaterial = buriedMaterial;
        materialOverridden = true;
    }

    /// <summary>清理对象池复用时残留的材质属性块。</summary>
    private void ClearVisualState()
    {
        spriteRenderer?.SetPropertyBlock(null);
    }

    /// <summary>卸载时恢复外壳原材质，避免普通 Prop 物品被污染。</summary>
    private void RestoreOriginalMaterial()
    {
        if (!materialOverridden || spriteRenderer == null)
            return;

        spriteRenderer.sharedMaterial = originalMaterial;
        originalMaterial = null;
        materialOverridden = false;
    }

    private void OnValidate()
    {
        buriedClip = Mathf.Clamp01(buriedClip);
        seedlingScale = Mathf.Clamp(seedlingScale, 0.01f, 1f);
        matureScale = Mathf.Max(seedlingScale, matureScale);
        growingVisualThreshold = Mathf.Clamp(growingVisualThreshold, 0.01f, 0.99f);
    }

    #endregion
}
