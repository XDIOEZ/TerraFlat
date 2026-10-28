using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>地面门和船上门共用双扇滑动表现，通行状态仍由各自业务控制。</summary>
public sealed class DoorSlideVisual : IDisposable
{
    #region 双扇滑动表现
    public const float SlideSeconds = .22f;
    private readonly SpriteRenderer source;
    private readonly SpriteRenderer[] leaves = new SpriteRenderer[2];
    private readonly Vector3[] closedOffsets = new Vector3[2];
    private readonly bool sourceEnabled;
    private float target, progress;
    private bool dirty = true;
    private readonly SortingGroup group;
    private readonly bool ownsGroup;
    private readonly int originalGroupLayer, originalGroupOrder;
    private readonly bool originalGroupEnabled;
    public float Progress => progress;

    public DoorSlideVisual(SpriteRenderer renderer, bool open)
    {
        source = renderer;
        group = renderer.GetComponent<SortingGroup>();
        if (group == null) { group = renderer.gameObject.AddComponent<SortingGroup>(); ownsGroup = true; }
        originalGroupLayer = group.sortingLayerID;
        originalGroupOrder = group.sortingOrder;
        originalGroupEnabled = group.enabled;
        sourceEnabled = renderer.enabled;
        Sprite sprite = renderer.sprite;
        Rect rect = sprite.rect;
        float splitWidth = Mathf.Floor(rect.width * .5f);
        for (int side = 0; side < 2; side++)
        {
            Rect half = new(rect.x + (side == 0 ? 0f : splitWidth), rect.y,
                side == 0 ? splitWidth : rect.width - splitWidth, rect.height);
            var leaf = new GameObject(side == 0 ? "左舱门" : "右舱门");
            leaf.transform.SetParent(renderer.transform, false);
            SpriteRenderer part = leaves[side] = leaf.AddComponent<SpriteRenderer>();
            part.sprite = Sprite.Create(sprite.texture, half, new Vector2(.5f, .5f),
                sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            part.sprite.name = sprite.name + "_DoorLeaf_" + side;
            part.sprite.hideFlags = HideFlags.DontSave;
            part.spriteSortPoint = SpriteSortPoint.Pivot;
            closedOffsets[side] = new Vector3((half.center.x - rect.x - sprite.pivot.x) / sprite.pixelsPerUnit,
                (half.center.y - rect.y - sprite.pivot.y) / sprite.pixelsPerUnit);
        }
        SetOpen(open, true);
    }

    public void SetOpen(bool open, bool immediate = false)
    {
        target = open ? 1f : 0f;
        if (immediate) progress = target;
        dirty = true;
        Advance(0f);
    }

    public bool Advance(float deltaTime)
    {
        if (source == null) return false;
        // 两扇门用同一脚点排序，滑向两侧时都盖在相邻墙面上。
        if (!group.enabled) group.enabled = true;
        if (group.sortingLayerID != source.sortingLayerID) group.sortingLayerID = source.sortingLayerID;
        int order = Mathf.Min(short.MaxValue, source.sortingOrder + 1);
        if (group.sortingOrder != order) group.sortingOrder = order;
        bool moving = progress != target;
        if (!dirty && !moving && !source.enabled && leaves[0].sharedMaterial == source.sharedMaterial &&
            leaves[0].color == source.color && leaves[0].sortingLayerID == source.sortingLayerID &&
            leaves[0].sortingOrder == source.sortingOrder) return false;
        progress = Mathf.MoveTowards(progress, target, Mathf.Max(0f, deltaTime) / SlideSeconds);
        float travel = source.sprite.rect.width / source.sprite.pixelsPerUnit * .55f;
        float slide = Mathf.SmoothStep(0f, 1f, progress);
        for (int side = 0; side < leaves.Length; side++)
        {
            SpriteRenderer leaf = leaves[side];
            leaf.sharedMaterial = source.sharedMaterial;
            leaf.color = source.color;
            leaf.sortingLayerID = source.sortingLayerID;
            leaf.sortingOrder = source.sortingOrder;
            leaf.transform.localPosition = closedOffsets[side] + Vector3.right * ((side == 0 ? -1f : 1f) * travel * slide);
        }
        source.enabled = false;
        dirty = false;
        return progress != target;
    }

    public void Dispose()
    {
        foreach (SpriteRenderer leaf in leaves)
            if (leaf != null)
            {
                leaf.gameObject.SetActive(false);
                Release(leaf.sprite);
                Release(leaf.gameObject);
            }
        if (source != null) source.enabled = sourceEnabled;
        if (group != null)
        {
            // 同帧重新启用时复用排序组，避免引用待销毁组件。
            if (ownsGroup) group.enabled = false;
            else
            {
                group.sortingLayerID = originalGroupLayer;
                group.sortingOrder = originalGroupOrder;
                group.enabled = originalGroupEnabled;
            }
        }
    }

    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
    #endregion
}
