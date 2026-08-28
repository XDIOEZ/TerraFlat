using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 交互目标的本地白色描边。组件运行时挂在目标 Item/视觉根节点上，
/// 每个源 SpriteRenderer 只创建一份略微放大的白色 Sprite 作为轮廓，
/// 不修改原始 SpriteRenderer 的材质。
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionTargetOutline : MonoBehaviour
{
    private const float DefaultThicknessPixels = 2f;

    private static readonly int BodyClipProperty = Shader.PropertyToID("_BodyClip");
    private static readonly int BodyMinVProperty = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxVProperty = Shader.PropertyToID("_BodyMaxV");
    private static Shader outlineShader;
    private static Material sharedOutlineMaterial;

    [SerializeField, Min(0.25f)]
    private float thicknessPixels = DefaultThicknessPixels;

    private readonly List<SpriteRenderer> sourceRenderers = new(8);
    private readonly List<OutlineEntry> outlineEntries = new(8);
    private readonly HashSet<SpriteRenderer> activeSourceRenderers = new();
    private MaterialPropertyBlock sourcePropertyBlock;
    private MaterialPropertyBlock outlinePropertyBlock;
    private bool highlighted;

    public bool IsHighlighted => highlighted;

    #region 生命周期

    /// <summary>在 Unity 生命周期内创建原生渲染参数容器。</summary>
    private void Awake()
    {
        sourcePropertyBlock = new MaterialPropertyBlock();
        outlinePropertyBlock = new MaterialPropertyBlock();
    }

    #endregion

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
    /// 同步单个白色副本，并按期望像素厚度在 X/Y 两轴分别放大。
    /// 缩放时补偿 Sprite Pivot，使放大围绕可见区域中心进行，而不是围绕 Pivot 偏移。
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
        outline.sortingOrder = source.sortingOrder == int.MinValue
            ? int.MinValue
            : source.sortingOrder - 1;
        outline.drawMode = source.drawMode;
        outline.size = source.size;
        outline.tileMode = source.tileMode;

        Vector3 outlineScale = CalculateOutlineScale(source);
        Vector3 visibleCenter = CalculateVisibleLocalCenter(source);
        outline.transform.localPosition = new Vector3(
            visibleCenter.x * (1f - outlineScale.x),
            visibleCenter.y * (1f - outlineScale.y),
            0f);
        outline.transform.localRotation = Quaternion.identity;
        outline.transform.localScale = outlineScale;
        SyncRendererClipState(source, outline);
        outline.enabled = highlighted && source.enabled &&
            source.gameObject.activeInHierarchy && source.sprite != null;
    }

    /// <summary>同步源渲染器的局部裁剪状态，避免描边重新显示已剔除的像素。</summary>
    private void SyncRendererClipState(SpriteRenderer source, SpriteRenderer outline)
    {
        Material sourceMaterial = source.sharedMaterial;
        if (sourceMaterial == null || !sourceMaterial.HasProperty(BodyClipProperty))
        {
            outline.SetPropertyBlock(null);
            return;
        }

        sourcePropertyBlock.Clear();
        source.GetPropertyBlock(sourcePropertyBlock);

        outlinePropertyBlock.Clear();
        outlinePropertyBlock.SetFloat(BodyClipProperty, sourcePropertyBlock.GetFloat(BodyClipProperty));
        outlinePropertyBlock.SetFloat(BodyMinVProperty, sourcePropertyBlock.GetFloat(BodyMinVProperty));
        outlinePropertyBlock.SetFloat(BodyMaxVProperty, sourcePropertyBlock.GetFloat(BodyMaxVProperty));
        outline.SetPropertyBlock(outlinePropertyBlock);
    }

    private Vector3 CalculateOutlineScale(SpriteRenderer source)
    {
        Sprite sprite = source.sprite;
        if (sprite == null)
            return Vector3.one;

        float pixelsPerUnit = Mathf.Max(0.01f, sprite.pixelsPerUnit);
        float localThickness = Mathf.Max(0.25f, thicknessPixels) / pixelsPerUnit;
        Vector2 renderedSize = GetRenderedLocalSize(source);

        float scaleX = 1f + (2f * localThickness / Mathf.Max(0.0001f, Mathf.Abs(renderedSize.x)));
        float scaleY = 1f + (2f * localThickness / Mathf.Max(0.0001f, Mathf.Abs(renderedSize.y)));
        return new Vector3(scaleX, scaleY, 1f);
    }

    private static Vector3 CalculateVisibleLocalCenter(SpriteRenderer source)
    {
        Sprite sprite = source.sprite;
        if (sprite == null)
            return Vector3.zero;

        Vector2 renderedSize = GetRenderedLocalSize(source);
        Vector2 rectSize = sprite.rect.size;
        float pivotX = rectSize.x > 0.0001f ? sprite.pivot.x / rectSize.x : 0.5f;
        float pivotY = rectSize.y > 0.0001f ? sprite.pivot.y / rectSize.y : 0.5f;

        float centerX = (0.5f - pivotX) * renderedSize.x;
        float centerY = (0.5f - pivotY) * renderedSize.y;
        if (source.flipX)
            centerX = -centerX;
        if (source.flipY)
            centerY = -centerY;

        return new Vector3(centerX, centerY, 0f);
    }

    private static Vector2 GetRenderedLocalSize(SpriteRenderer source)
    {
        if (source.sprite == null)
            return Vector2.one;

        return source.drawMode == SpriteDrawMode.Simple
            ? (Vector2)source.sprite.bounds.size
            : source.size;
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

    private sealed class OutlineEntry
    {
        public SpriteRenderer Source;
        public SpriteRenderer Renderer;
    }
}
