using System;
using UnityEngine;

/// <summary>
/// 作物表现模块：只负责把作物精灵裁成半埋状态，不参与种植、成长或收获交互。
/// 使用 Sprite-Lit-Master 的 BodyClip 保留地上部分，具体作物的成长缩放仍由 Mod_Grow 控制。
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

    #endregion

    #region 运行时

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Material originalMaterial;
    private bool materialOverridden;

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

        ApplyBuriedMaterial();
        ApplyVisualState();
    }

    public override void Save()
    {
        // 表现参数来自 ItemDefinition，运行时只写入材质属性块，不产生独立状态。
    }

    public override void Unload()
    {
        ClearVisualState();
        RestoreOriginalMaterial();
        spriteRenderer = null;
        propertyBlock = null;
    }

    private void OnDisable()
    {
        ClearVisualState();
        RestoreOriginalMaterial();
    }

    private void OnDestroy()
    {
        Unload();
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

    #endregion
}
