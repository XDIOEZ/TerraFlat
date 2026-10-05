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
    private bool highlighted;

    public bool IsHighlighted => highlighted;

    #endregion

    #region 生命周期

    /// <summary>在 Unity 生命周期内创建原生渲染参数容器。</summary>
    private void Awake()
    {
        sourcePropertyBlock = new MaterialPropertyBlock();
        outlinePropertyBlock = new MaterialPropertyBlock();
    }

    #endregion

    #region 交互目标入口

    /// <summary>为交互组件所属的 Item 获取或创建本地描边控制器。</summary>
    public static InteractionTargetOutline GetOrCreate(Component interactable)
    {
        if (interactable == null)
            return null;

        Item targetItem = interactable.GetComponentInParent<Item>();
        GameObject targetObject = targetItem != null
            ? targetItem.gameObject
            : ResolveVisualRoot(interactable);

        if (targetObject == null)
            return null;

        return targetObject.GetComponent<InteractionTargetOutline>() ??
            targetObject.AddComponent<InteractionTargetOutline>();
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
        if (highlighted == value)
            return;

        highlighted = value;
        if (highlighted)
            RefreshOutlineRenderers();
        else
            DisableOutlineRenderers();
    }

    #endregion

    #region 描边同步与回收

    private void LateUpdate()
    {
        if (highlighted)
            RefreshOutlineRenderers();
    }

    private void OnDisable()
    {
        highlighted = false;
        DisableOutlineRenderers();
    }

    private void RefreshOutlineRenderers()
    {
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

        outline.sharedMaterial = material;
        outline.sprite = source.sprite;
        outline.color = new Color(1f, 1f, 1f, source.color.a);
        outline.flipX = source.flipX;
        outline.flipY = source.flipY;
        outline.maskInteraction = source.maskInteraction;
        outline.sortingLayerID = source.sortingLayerID;
        outline.sortingOrder = Mathf.Min(short.MaxValue, source.sortingOrder + 1);
        outline.drawMode = source.drawMode;
        outline.size = source.size;
        outline.tileMode = source.tileMode;

        outline.transform.localPosition = Vector3.zero;
        outline.transform.localRotation = Quaternion.identity;
        outline.transform.localScale = Vector3.one;
        SyncRendererClipState(source, outline);
        outline.enabled = highlighted && source.enabled &&
            source.gameObject.activeInHierarchy && source.sprite != null;
    }

    /// <summary>同步贴图、图集边界和局部裁剪，避免描边采到邻图或显示已剔除像素。</summary>
    private void SyncRendererClipState(SpriteRenderer source, SpriteRenderer outline)
    {
        Material sourceMaterial = source.sharedMaterial;
        sourcePropertyBlock.Clear();
        source.GetPropertyBlock(sourcePropertyBlock);

        outlinePropertyBlock.Clear();
        // 写入裁剪参数会替换代理 Renderer 的属性块，必须同时恢复 Sprite 的逐渲染器贴图。
        Sprite sprite = outline.sprite;
        if (sprite != null)
        {
            outlinePropertyBlock.SetTexture(MainTextureProperty, sprite.texture);
            Texture2D alpha = sprite.associatedAlphaSplitTexture;
            outlinePropertyBlock.SetTexture(AlphaTextureProperty, alpha != null ? alpha : Texture2D.whiteTexture);
            outlinePropertyBlock.SetFloat(ExternalAlphaProperty, alpha != null ? 1f : 0f);
            outlinePropertyBlock.SetVector(SpriteUvProperty, DataUtility.GetOuterUV(sprite));
        }
        outlinePropertyBlock.SetFloat(BodyClipProperty, ReadSourceFloat(sourceMaterial, BodyClipProperty, 0f));
        outlinePropertyBlock.SetFloat(BodyMinVProperty, ReadSourceFloat(sourceMaterial, BodyMinVProperty, 0f));
        outlinePropertyBlock.SetFloat(BodyMaxVProperty, ReadSourceFloat(sourceMaterial, BodyMaxVProperty, 1f));
        outline.SetPropertyBlock(outlinePropertyBlock);
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
        if (entry?.Renderer != null)
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
    }

    #endregion
}
