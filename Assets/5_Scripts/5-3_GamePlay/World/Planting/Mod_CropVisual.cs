using System;
using UnityEngine;

/// <summary>
/// 作物表现模块：把作物精灵裁成半埋状态，并把 Mod_Crop 的连续进度映射为缩放。
/// 不参与种植、成长结算或收获交互，对象卸载时恢复外壳原始材质与缩放。
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

    #endregion

    #region 运行时

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Material originalMaterial;
    private bool materialOverridden;
    private Mod_Crop cropModule;
    private Vector3 originalLocalScale;
    private bool scaleCaptured;

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
        scaleCaptured = true;
        ApplyBuriedMaterial();
        ApplyVisualState();
        cropModule.GrowthChanged += HandleGrowthChanged;
        ApplyGrowthScale(cropModule.NormalizedGrowth);
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
        RestoreOriginalMaterial();
        spriteRenderer = null;
        propertyBlock = null;
    }

    private void OnDisable()
    {
        UnbindCrop();
        ClearVisualState();
        RestoreOriginalScale();
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
            ApplyGrowthScale(normalizedGrowth);
    }

    /// <summary>在幼苗与成熟缩放之间连续插值。</summary>
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
        originalLocalScale = Vector3.one;
        scaleCaptured = false;
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
    }

    #endregion
}
