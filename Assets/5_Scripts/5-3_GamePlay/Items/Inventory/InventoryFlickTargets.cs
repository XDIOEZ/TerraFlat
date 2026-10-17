using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>由正式库存面板登记甩动目标，按方向选择一个当前可接收的真实库存。</summary>
public sealed class InventoryFlickTargets : MonoBehaviour
{
    #region 面板生命周期登记
    private static readonly List<InventoryFlickTargets> active = new();
    private readonly List<Inventory> inventories = new();
    private BasePanel panel;
    private readonly Vector3[] corners = new Vector3[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    /// <summary>返回当前登记快照，诊断或隔离测试可临时禁用组件后原样恢复。</summary>
    public static InventoryFlickTargets[] GetActivePanels() => active.ToArray();

    public void Bind(Inventory inventory, BasePanel owner)
    {
        if (panel == null)
        {
            panel = owner;
            panel.Opened += RefreshRegistration;
            panel.Closed += RefreshRegistration;
        }
        if (!inventories.Contains(inventory)) inventories.Add(inventory);
        RefreshRegistration();
    }

    public void Unbind(Inventory inventory)
    {
        inventories.Remove(inventory);
        RefreshRegistration();
    }

    private void RefreshRegistration()
    {
        bool visible = isActiveAndEnabled && panel != null && panel.IsOpen() && inventories.Count > 0;
        if (visible && !active.Contains(this)) active.Add(this);
        else if (!visible) active.Remove(this);
    }

    private void OnEnable() => RefreshRegistration();
    private void OnDisable() => active.Remove(this);
    private void OnDestroy()
    {
        active.Remove(this);
        if (panel != null)
        {
            panel.Opened -= RefreshRegistration;
            panel.Closed -= RefreshRegistration;
        }
        inventories.Clear();
    }
    #endregion

    #region 方向选择与接收资格
    // 同角度时保留登记顺序，距离不会抢在角度之前影响选择，也不附加硬性方向扇区。
    public static float DirectionScore(Vector2 origin, Vector2 direction, Vector2 target)
    {
        if (!Finite(origin) || !Finite(direction) || !Finite(target)) return float.NegativeInfinity;
        Vector2 offset = target - origin;
        if (!Finite(offset) || float.IsInfinity(direction.sqrMagnitude) || float.IsInfinity(offset.sqrMagnitude) ||
            direction.sqrMagnitude < .0001f || offset.sqrMagnitude < .0001f) return float.NegativeInfinity;
        return Vector2.Dot(direction.normalized, offset.normalized);
    }

    public static int SelectClosestDirection(Vector2 origin, Vector2 direction, IReadOnlyList<Vector2> targets)
    {
        int best = -1;
        float bestScore = float.NegativeInfinity;
        if (targets == null) return best;
        for (int i = 0; i < targets.Count; i++)
        {
            float score = DirectionScore(origin, direction, targets[i]);
            if (score <= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    public static Inventory Resolve(Inventory source, int sourceIndex, Vector2 origin, Vector2 direction)
    {
        if (source?.Data?.GetItemSlot(sourceIndex)?.itemData == null || !Finite(origin) ||
            !Finite(direction) || direction.sqrMagnitude < .0001f) return null;
        var candidates = new List<Inventory>();
        var centers = new List<Vector2>();
        foreach (InventoryFlickTargets owner in active)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.panel == null || !owner.panel.IsOpen()) continue;
            foreach (Inventory target in owner.inventories)
            {
                if (target == source || target is Inventory_Hand || target?.Data == null ||
                    ReferenceEquals(target.Data, source?.Data) || target.Data.Name == ModText.Hand ||
                    !source.CanQuickTransferTo(sourceIndex, target) || !owner.TryGetVisibleCenter(target, out Vector2 center)) continue;
                candidates.Add(target);
                centers.Add(center);
            }
        }
        int selected = SelectClosestDirection(origin, direction, centers);
        return selected >= 0 ? candidates[selected] : null;
    }

    private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
    #endregion

    #region 真实库存可见区域
    private Rect ScreenRect(RectTransform rect, Camera camera)
    {
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect Intersect(Rect left, Rect right) => Rect.MinMaxRect(
        Mathf.Max(left.xMin, right.xMin), Mathf.Max(left.yMin, right.yMin),
        Mathf.Min(left.xMax, right.xMax), Mathf.Min(left.yMax, right.yMax));

    private bool TryGetVisibleCenter(Inventory inventory, out Vector2 center)
    {
        center = default;
        bool found = false;
        Rect bounds = default;
        if (inventory.itemSlot_UI == null) return false;
        for (int i = 0; i < inventory.itemSlot_UI.Count; i++)
        {
            ItemSlot_UI slot = inventory.itemSlot_UI[i];
            if (slot == null || slot.slotIndex != i || !slot.isActiveAndEnabled ||
                !ReferenceEquals(slot.GetBoundSlotData(), inventory.Data.GetItemSlot(i)) ||
                !slot.transform.IsChildOf(panel.transform) || slot.transform is not RectTransform rect) continue;
            Canvas canvas = slot.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled || !canvas.rootCanvas.isActiveAndEnabled) continue;
            bool hidden = false;
            foreach (CanvasGroup group in slot.GetComponentsInParent<CanvasGroup>())
                if (group.alpha <= .001f) { hidden = true; break; }
            if (hidden) continue;
            Camera camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            Rect visible = Intersect(ScreenRect(rect, camera), new Rect(0f, 0f, Screen.width, Screen.height));
            foreach (RectMask2D mask in slot.GetComponentsInParent<RectMask2D>())
                if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
            foreach (Mask mask in slot.GetComponentsInParent<Mask>())
                if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
            if (visible.width <= 0f || visible.height <= 0f) continue;
            bounds = found ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, visible.xMin), Mathf.Min(bounds.yMin, visible.yMin),
                Mathf.Max(bounds.xMax, visible.xMax), Mathf.Max(bounds.yMax, visible.yMax)) : visible;
            found = true;
        }
        center = bounds.center;
        return found;
    }
    #endregion
}
