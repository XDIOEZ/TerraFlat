using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 交互目标的本地白色描边。组件运行时挂在目标 Item/视觉根节点上，
/// 通过多份白色 Sprite 轻微错位形成轮廓，不修改原始 SpriteRenderer 的材质。
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionTargetOutline : MonoBehaviour
{
    private const float DefaultThicknessPixels = 2f;
    private const int OutlineCopyCount = 8;

    private static readonly Vector2[] OutlineDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(-1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
        new Vector2(0.7071f, 0.7071f),
        new Vector2(-0.7071f, 0.7071f),
        new Vector2(0.7071f, -0.7071f),
        new Vector2(-0.7071f, -0.7071f)
    };

    private static Shader outlineShader;
    private static Material sharedOutlineMaterial;

    [SerializeField, Min(0.25f)]
    private float thicknessPixels = DefaultThicknessPixels;

    private readonly List<SpriteRenderer> sourceRenderers = new(8);
    private readonly List<OutlineEntry> outlineEntries = new(8);
    private readonly HashSet<SpriteRenderer> activeSourceRenderers = new();
    private bool highlighted;

    public bool IsHighlighted => highlighted;

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
                SyncOutlineRenderers(source, entry, material);
        }

        for (int i = outlineEntries.Count - 1; i >= 0; i--)
        {
            OutlineEntry entry = outlineEntries[i];
            if (entry.Source == null || entry.Renderers == null)
            {
                DestroyEntryRenderers(entry);
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
            if (existing.Source == source && existing.Renderers != null)
                return existing;

            if (existing.Source == null || existing.Renderers == null)
            {
                DestroyEntryRenderers(existing);
                existing.Source = source;
                existing.Renderers = CreateOutlineRenderers(source);
                return existing;
            }
        }

        OutlineEntry created = new OutlineEntry
        {
            Source = source,
            Renderers = CreateOutlineRenderers(source)
        };
        outlineEntries.Add(created);
        return created;
    }

    /// <summary>在源 Sprite 周围放置 8 个白色副本，形成稳定的像素级轮廓。</summary>
    private void SyncOutlineRenderers(
        SpriteRenderer source,
        OutlineEntry entry,
        Material material)
    {
        float pixelsPerUnit = source.sprite != null
            ? Mathf.Max(0.01f, source.sprite.pixelsPerUnit)
            : 1f;
        float worldThickness = Mathf.Max(0.25f, thicknessPixels) / pixelsPerUnit;

        for (int i = 0; i < entry.Renderers.Length; i++)
        {
            SpriteRenderer outline = entry.Renderers[i];
            if (outline == null)
                continue;

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

            // 描边渲染器是源节点的子节点，继承源节点的旋转与缩放；
            // InverseTransformVector 保证不同缩放下仍然是固定像素厚度。
            Vector3 worldOffset = OutlineDirections[i] * worldThickness;
            outline.transform.localPosition = source.transform.InverseTransformVector(worldOffset);
            outline.transform.localRotation = Quaternion.identity;
            outline.transform.localScale = Vector3.one;
            outline.enabled = highlighted && source.enabled &&
                source.gameObject.activeInHierarchy && source.sprite != null;
        }
    }

    private static SpriteRenderer[] CreateOutlineRenderers(SpriteRenderer source)
    {
        SpriteRenderer[] renderers = new SpriteRenderer[OutlineCopyCount];
        for (int i = 0; i < renderers.Length; i++)
        {
            GameObject outlineObject = new GameObject("Interaction Outline");
            outlineObject.hideFlags = HideFlags.DontSave;
            outlineObject.layer = source.gameObject.layer;
            outlineObject.transform.SetParent(source.transform, worldPositionStays: false);

            SpriteRenderer outline = outlineObject.AddComponent<SpriteRenderer>();
            outline.hideFlags = HideFlags.DontSave;
            outline.enabled = false;
            renderers[i] = outline;
        }

        return renderers;
    }

    private void DisableOutlineRenderers()
    {
        for (int i = 0; i < outlineEntries.Count; i++)
            SetEntryEnabled(outlineEntries[i], false);
    }

    private static void SetEntryEnabled(OutlineEntry entry, bool enabled)
    {
        if (entry?.Renderers == null)
            return;

        for (int i = 0; i < entry.Renderers.Length; i++)
        {
            if (entry.Renderers[i] != null)
                entry.Renderers[i].enabled = enabled;
        }
    }

    private bool IsOutlineRenderer(SpriteRenderer candidate)
    {
        for (int i = 0; i < outlineEntries.Count; i++)
        {
            SpriteRenderer[] renderers = outlineEntries[i].Renderers;
            if (renderers == null)
                continue;

            for (int j = 0; j < renderers.Length; j++)
            {
                if (renderers[j] == candidate)
                    return true;
            }
        }

        return false;
    }

    private static void DestroyEntryRenderers(OutlineEntry entry)
    {
        if (entry?.Renderers == null)
            return;

        for (int i = 0; i < entry.Renderers.Length; i++)
        {
            if (entry.Renderers[i] != null)
                Destroy(entry.Renderers[i].gameObject);
        }
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
        public SpriteRenderer[] Renderers;
    }
}
