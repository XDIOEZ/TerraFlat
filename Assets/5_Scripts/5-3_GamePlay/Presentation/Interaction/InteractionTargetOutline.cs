using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

/// <summary>
/// 交互目标的本地白色描边。组件运行时挂在目标 Item/视觉根节点上，
/// 每个源 SpriteRenderer 只创建一份同尺寸的轮廓代理，以屏幕像素计算白边，
/// 不修改原始 SpriteRenderer 的材质。
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionTargetOutline : MonoBehaviour
{
    #region 描边参数与资源

    private static readonly int MainTextureProperty = Shader.PropertyToID("_MainTex");
    private static readonly int AlphaTextureProperty = Shader.PropertyToID("_AlphaTex");
    private static readonly int ExternalAlphaProperty = Shader.PropertyToID("_EnableExternalAlpha");
    private static readonly int SpriteUvProperty = Shader.PropertyToID("_OutlineSpriteUV");
    private static readonly int BodyClipProperty = Shader.PropertyToID("_BodyClip");
    private static readonly int BodyMinVProperty = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxVProperty = Shader.PropertyToID("_BodyMaxV");
    private static Shader outlineShader;
    private static Material sharedOutlineMaterial;

    private readonly List<SpriteRenderer> sourceRenderers = new(8);
    private readonly List<OutlineEntry> outlineEntries = new(8);
    private readonly HashSet<SpriteRenderer> activeSourceRenderers = new();
    private MaterialPropertyBlock sourcePropertyBlock;
    private MaterialPropertyBlock outlinePropertyBlock;
    private readonly HashSet<Component> interactionHighlightSources = new();
    private bool manuallyHighlighted;
    private bool highlighted;

    public bool IsHighlighted => highlighted;
    public bool IsManuallyHighlighted => manuallyHighlighted;
    public int InteractionHighlightRequestCount => interactionHighlightSources.Count;

    #endregion

    #region 生命周期

    /// <summary>在 Unity 生命周期内创建原生渲染参数容器。</summary>
    private void Awake()
    {
        EnsureOutlinePropertyBlocks();
    }

    private void EnsureOutlinePropertyBlocks()
    {
        sourcePropertyBlock ??= new MaterialPropertyBlock();
        outlinePropertyBlock ??= new MaterialPropertyBlock();
    }

    #endregion

    #region 交互目标入口

    /// <summary>为交互组件所属的 Item 获取或创建本地描边控制器。</summary>
    public static InteractionTargetOutline GetOrCreate(Component interactable)
    {
        if (interactable == null)
            return null;

        GameObject targetObject = ResolveTargetObject(interactable);

        if (targetObject == null)
            return null;

        return targetObject.GetComponent<InteractionTargetOutline>() ??
            targetObject.AddComponent<InteractionTargetOutline>();
    }

    /// <summary>取消请求时只查现有控制器，不为清理操作创建组件。</summary>
    public static InteractionTargetOutline GetExisting(Component target)
    {
        if (target == null) return null;
        GameObject targetObject = ResolveTargetObject(target);
        return targetObject != null ? targetObject.GetComponent<InteractionTargetOutline>() : null;
    }

    private static GameObject ResolveTargetObject(Component target)
    {
        Item item = target.GetComponentInParent<Item>();
        return item != null ? item.gameObject : ResolveVisualRoot(target);
    }

    /// <summary>无 Item 外壳时，向上找到最近的可见 Sprite 根节点。</summary>
    private static GameObject ResolveVisualRoot(Component interactable)
    {
        Transform current = interactable.transform;
        while (current != null)
        {
            if (current.GetComponentInChildren<SpriteRenderer>(includeInactive: true) != null)
                return current.gameObject;

            current = current.parent;
        }

        return interactable.gameObject;
    }

    /// <summary>开启或关闭当前目标的本地白色描边。</summary>
    public void SetHighlighted(bool value)
    {
        manuallyHighlighted = value;
        ApplyCombinedHighlightRequests();
    }

    /// <summary>交互来源各自登记，取消其中一个不会清掉其它高亮请求。</summary>
    public void SetInteractionHighlighted(Component source, bool value)
    {
        if (ReferenceEquals(source, null) || (value && source == null)) return;
        if (value) interactionHighlightSources.Add(source);
        else interactionHighlightSources.Remove(source);
        ApplyCombinedHighlightRequests();
    }

    internal void ClearHighlightRequests()
    {
        manuallyHighlighted = false;
        interactionHighlightSources.Clear();
        ApplyCombinedHighlightRequests();
    }

    private void ApplyCombinedHighlightRequests()
    {
        bool value = manuallyHighlighted || interactionHighlightSources.Count > 0;
        if (highlighted == value) return;
        highlighted = value;
        if (highlighted)
        {
            enabled = true;
            RefreshHighlightedRenderers();
        }
        else
        {
            DisableOutlineRenderers();
            WorldRenderingManager.ExistingInstance?.ForgetHighlightController(this);
            enabled = false;
        }
    }

    internal void RefreshHighlightedRenderers()
    {
        interactionHighlightSources.RemoveWhere(static source => source == null);
        ApplyCombinedHighlightRequests();
        if (!highlighted) return;
        // 再次选中或结构重绑时重交裁剪参数，兼容资源重载后的代理恢复。
        for (int i = 0; i < outlineEntries.Count; i++) outlineEntries[i].HasClipState = false;
        RefreshOutlineRenderers();
    }

    #endregion

    #region 描边同步与回收

    private void LateUpdate()
    {
        if (interactionHighlightSources.RemoveWhere(static source => source == null) > 0)
            ApplyCombinedHighlightRequests();
        if (highlighted)
            RefreshOutlineRenderers();
    }

    private void OnDisable()
    {
        manuallyHighlighted = false;
        interactionHighlightSources.Clear();
        highlighted = false;
        DisableOutlineRenderers();
        WorldRenderingManager.ExistingInstance?.ForgetHighlightController(this);
    }

    private void RefreshOutlineRenderers()
    {
        EnsureOutlinePropertyBlocks();
        if (!InteractionOutlineSettings.Enabled)
        {
            DisableOutlineRenderers();
            return;
        }

        Material material = GetSharedOutlineMaterial();
        if (material == null)
        {
            DisableOutlineRenderers();
            return;
        }

        sourceRenderers.Clear();
        GetComponentsInChildren(includeInactive: true, sourceRenderers);
        activeSourceRenderers.Clear();

        for (int i = 0; i < sourceRenderers.Count; i++)
        {
            SpriteRenderer source = sourceRenderers[i];
            if (source == null || IsOutlineRenderer(source))
                continue;

            // 建筑主体已合入行网格时直接更新该部件的描边状态，不再补绘整张白色 Sprite。
            if (BuildingDepthMeshBridge.IsProjected(source))
            {
                BuildingDepthMeshBridge.SetHighlighted(source, true);
                continue;
            }

            activeSourceRenderers.Add(source);
            OutlineEntry entry = GetOrCreateEntry(source);
            if (entry != null)
                SyncOutlineRenderer(source, entry, material);
        }

        for (int i = outlineEntries.Count - 1; i >= 0; i--)
        {
            OutlineEntry entry = outlineEntries[i];
            if (entry.Source == null || entry.Renderer == null)
            {
                DestroyEntryRenderer(entry);
                outlineEntries.RemoveAt(i);
                continue;
            }

            if (!activeSourceRenderers.Contains(entry.Source))
                SetEntryEnabled(entry, false);
        }
    }

    private OutlineEntry GetOrCreateEntry(SpriteRenderer source)
    {
        for (int i = 0; i < outlineEntries.Count; i++)
        {
            OutlineEntry existing = outlineEntries[i];
            if (existing.Source == source && existing.Renderer != null)
                return existing;

            if (existing.Source == null || existing.Renderer == null)
            {
                DestroyEntryRenderer(existing);
                existing.Source = source;
                existing.Renderer = CreateOutlineRenderer(source);
                existing.HasClipState = false;
                return existing;
            }
        }

        OutlineEntry created = new OutlineEntry
        {
            Source = source,
            Renderer = CreateOutlineRenderer(source)
        };
        outlineEntries.Add(created);
        return created;
    }

    /// <summary>
    /// 同步同尺寸的轮廓代理，实际白边宽度由 Shader 按屏幕像素计算。
    /// </summary>
    private void SyncOutlineRenderer(
        SpriteRenderer source,
        OutlineEntry entry,
        Material material)
    {
        SpriteRenderer outline = entry.Renderer;
        if (outline == null)
            return;

        // 每帧仍读取源动画和排序，只有实际变化才跨入 Unity 原生属性写入。
        if (outline.sharedMaterial != material)
        {
            outline.sharedMaterial = material;
            entry.HasClipState = false;
        }
        Sprite sprite = source.sprite;
        if (outline.sprite != sprite) outline.sprite = sprite;
        Color outlineColor = new(1f, 1f, 1f, source.color.a);
        if (!outline.color.Equals(outlineColor)) outline.color = outlineColor;
        if (outline.flipX != source.flipX) outline.flipX = source.flipX;
        if (outline.flipY != source.flipY) outline.flipY = source.flipY;
        if (outline.maskInteraction != source.maskInteraction) outline.maskInteraction = source.maskInteraction;
        if (outline.sortingLayerID != source.sortingLayerID) outline.sortingLayerID = source.sortingLayerID;
        int order = Mathf.Min(short.MaxValue, source.sortingOrder + 1);
        if (outline.sortingOrder != order) outline.sortingOrder = order;
        if (outline.drawMode != source.drawMode) outline.drawMode = source.drawMode;
        if (!outline.size.Equals(source.size)) outline.size = source.size;
        if (outline.tileMode != source.tileMode) outline.tileMode = source.tileMode;

        SyncRendererClipState(source, entry);
        bool visible = highlighted && source.enabled &&
            source.gameObject.activeInHierarchy && sprite != null;
        if (outline.enabled != visible) outline.enabled = visible;
    }

    /// <summary>同步贴图、图集边界和局部裁剪，避免描边采到邻图或显示已剔除像素。</summary>
    private void SyncRendererClipState(SpriteRenderer source, OutlineEntry entry)
    {
        Material sourceMaterial = source.sharedMaterial;
        sourcePropertyBlock.Clear();
        source.GetPropertyBlock(sourcePropertyBlock);

        // MPB 仍每帧读取，保证角色受伤/淹没等动态裁剪即时生效；只省掉未变化的代理提交。
        float bodyClip = ReadSourceFloat(sourceMaterial, BodyClipProperty, 0f);
        float bodyMinV = ReadSourceFloat(sourceMaterial, BodyMinVProperty, 0f);
        float bodyMaxV = ReadSourceFloat(sourceMaterial, BodyMaxVProperty, 1f);
        Sprite sprite = entry.Renderer.sprite;
        if (entry.HasClipState && entry.ClipSprite == sprite &&
            entry.BodyClip.Equals(bodyClip) && entry.BodyMinV.Equals(bodyMinV) &&
            entry.BodyMaxV.Equals(bodyMaxV))
            return;

        outlinePropertyBlock.Clear();
        // 写入裁剪参数会替换代理 Renderer 的属性块，必须同时恢复 Sprite 的逐渲染器贴图。
        if (sprite != null)
        {
            outlinePropertyBlock.SetTexture(MainTextureProperty, sprite.texture);
            Texture2D alpha = sprite.associatedAlphaSplitTexture;
            outlinePropertyBlock.SetTexture(AlphaTextureProperty, alpha != null ? alpha : Texture2D.whiteTexture);
            outlinePropertyBlock.SetFloat(ExternalAlphaProperty, alpha != null ? 1f : 0f);
            outlinePropertyBlock.SetVector(SpriteUvProperty, DataUtility.GetOuterUV(sprite));
        }
        outlinePropertyBlock.SetFloat(BodyClipProperty, bodyClip);
        outlinePropertyBlock.SetFloat(BodyMinVProperty, bodyMinV);
        outlinePropertyBlock.SetFloat(BodyMaxVProperty, bodyMaxV);
        entry.Renderer.SetPropertyBlock(outlinePropertyBlock);
        entry.ClipSprite = sprite;
        entry.BodyClip = bodyClip;
        entry.BodyMinV = bodyMinV;
        entry.BodyMaxV = bodyMaxV;
        entry.HasClipState = true;
    }

    private float ReadSourceFloat(Material sourceMaterial, int property, float fallback)
    {
        if (sourcePropertyBlock.HasProperty(property))
            return sourcePropertyBlock.GetFloat(property);
        return sourceMaterial != null && sourceMaterial.HasProperty(property)
            ? sourceMaterial.GetFloat(property)
            : fallback;
    }

    private static SpriteRenderer CreateOutlineRenderer(SpriteRenderer source)
    {
        GameObject outlineObject = new GameObject("Interaction Outline");
        outlineObject.hideFlags = HideFlags.DontSave;
        outlineObject.layer = source.gameObject.layer;
        outlineObject.transform.SetParent(source.transform, worldPositionStays: false);
        // 代理相对源 Sprite 的姿态从创建起固定，后续跟随由父子 Transform 自动完成。
        outlineObject.transform.localPosition = Vector3.zero;
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one;

        // 描边独立读取主体裁剪，不进入角色 MPB 合成，避免外部写回覆盖稀疏缓存。
        outlineObject.AddComponent<ActorRenderEffectExclude>();
        SpriteRenderer outline = outlineObject.AddComponent<SpriteRenderer>();
        outline.hideFlags = HideFlags.DontSave;
        outline.enabled = false;
        return outline;
    }

    private void DisableOutlineRenderers()
    {
        foreach (SpriteRenderer source in sourceRenderers)
            BuildingDepthMeshBridge.SetHighlighted(source, false);
        for (int i = 0; i < outlineEntries.Count; i++)
            SetEntryEnabled(outlineEntries[i], false);
    }

    private static void SetEntryEnabled(OutlineEntry entry, bool enabled)
    {
        if (entry?.Renderer != null && entry.Renderer.enabled != enabled)
            entry.Renderer.enabled = enabled;
    }

    private bool IsOutlineRenderer(SpriteRenderer candidate)
    {
        for (int i = 0; i < outlineEntries.Count; i++)
        {
            if (outlineEntries[i].Renderer == candidate)
                return true;
        }

        return false;
    }

    private static void DestroyEntryRenderer(OutlineEntry entry)
    {
        if (entry?.Renderer != null)
            Destroy(entry.Renderer.gameObject);
    }

    /// <summary>优先使用项目内白色轮廓 Shader，再回退到 URP/内置 Sprite Shader。</summary>
    private static Material GetSharedOutlineMaterial()
    {
        if (sharedOutlineMaterial != null)
            return sharedOutlineMaterial;

        outlineShader = Resources.Load<Shader>("Shaders/InteractionOutline") ??
            Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
            Shader.Find("Sprites/Default");
        if (outlineShader == null)
            return null;

        sharedOutlineMaterial = new Material(outlineShader)
        {
            name = "Interaction Target Outline (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedOutlineMaterial;
    }

    #endregion

    #region 代理记录

    private sealed class OutlineEntry
    {
        public SpriteRenderer Source;
        public SpriteRenderer Renderer;
        public Sprite ClipSprite;
        public float BodyClip;
        public float BodyMinV;
        public float BodyMaxV;
        public bool HasClipState;
    }

    #endregion
}
