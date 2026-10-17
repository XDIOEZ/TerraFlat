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
    private readonly List<UIWindowDragSurface> windowSurfaces = new();

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
        if (!inventories.Contains(inventory))
        {
            inventories.Add(inventory);
            panel.GetComponentsInChildren(true, windowSurfaces);
        }
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
        windowSurfaces.Clear();
    }
    #endregion

    #region 方向选择与接收资格
    public const float DirectionEdgeToleranceDegrees = 10f;

    // 资格由窗口边界判断，合格目标仍按槽区中心夹角排序，同角度保留登记顺序。
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
            !Finite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f) return null;
        var candidates = new List<Inventory>();
        var centers = new List<Vector2>();
        foreach (InventoryFlickTargets owner in active)
        {
            if (owner == null) continue;
            foreach (Inventory target in owner.inventories)
            {
                if (!owner.TryGetTargetBounds(source, target, out Rect bounds) ||
                    !owner.IsWithinTargetDirection(target, bounds, origin, direction) ||
                    !source.CanQuickTransferTo(sourceIndex, target)) continue;
                candidates.Add(target);
                centers.Add(bounds.center);
            }
        }
        int selected = SelectClosestDirection(origin, direction, centers);
        return selected >= 0 ? candidates[selected] : null;
    }

    // 开关只看其他目标库存界面是否存在，满格仍按已有甩动规则拒绝并留在源槽。
    public static bool HasVisibleTarget(Inventory source)
    {
        if (source?.Data == null) return false;
        foreach (InventoryFlickTargets owner in active)
        {
            if (owner == null) continue;
            foreach (Inventory target in owner.inventories)
                if (owner.TryGetTargetBounds(source, target, out _)) return true;
        }
        return false;
    }

    // 角度资格不看容量，方向内已满的目标仍消费甩动并把物品留在原槽。
    public static bool HasTargetInDirection(Inventory source, Vector2 origin, Vector2 direction)
    {
        if (source?.Data == null || !Finite(origin) || !Finite(direction) ||
            float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f) return false;
        foreach (InventoryFlickTargets owner in active)
        {
            if (owner == null) continue;
            foreach (Inventory target in owner.inventories)
                if (owner.TryGetTargetBounds(source, target, out Rect bounds) &&
                    owner.IsWithinTargetDirection(target, bounds, origin, direction)) return true;
        }
        return false;
    }

    private bool TryGetTargetBounds(Inventory source, Inventory target, out Rect bounds)
    {
        bounds = default;
        return isActiveAndEnabled && panel != null && panel.IsOpen() && target != source &&
               target is not Inventory_Hand && target?.Data != null &&
               !ReferenceEquals(target.Data, source?.Data) && target.Data.Name != ModText.Hand &&
               TryGetVisibleBounds(target, null, out bounds);
    }

    private bool IsWithinTargetDirection(Inventory inventory, Rect slotBounds, Vector2 origin, Vector2 direction)
    {
        // 关闭角度限制只放行方向资格，可见目标、容量和最近夹角排序仍由原流程检查。
        if (!InventoryFlickUserSettings.AngleLimitEnabled) return true;
        bool hasWindow = false;
        foreach (UIWindowDragSurface surface in windowSurfaces)
        {
            RectTransform window = surface != null ? surface.WindowRect : null;
            if (window == null || !TryGetVisibleRect(window, out Rect visible) ||
                !TryGetVisibleBounds(inventory, window, out _)) continue;
            hasWindow = true;
            if (IsDirectionWithinTarget(origin, direction, visible)) return true;
        }
        return !hasWindow && IsDirectionWithinTarget(origin, direction, slotBounds);
    }

    /// <summary>从按下点看向真实矩形，边缘额外放宽十度；起点已在矩形内时允许任意有效方向。</summary>
    public static bool IsDirectionWithinTarget(Vector2 origin, Vector2 direction, Rect target)
    {
        if (!Finite(origin) || !Finite(direction) || !Finite(new Vector2(target.xMin, target.yMin)) ||
            !Finite(new Vector2(target.xMax, target.yMax)) || target.width <= 0f || target.height <= 0f ||
            float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f) return false;
        if (origin.x >= target.xMin && origin.x <= target.xMax &&
            origin.y >= target.yMin && origin.y <= target.yMax) return true;
        double near = 0d, far = double.PositiveInfinity;
        if (IntersectRayAxis(origin.x, direction.x, target.xMin, target.xMax, ref near, ref far) &&
            IntersectRayAxis(origin.y, direction.y, target.yMin, target.yMax, ref near, ref far)) return true;
        return WithinEdge(new Vector2(target.xMin, target.yMin)) || WithinEdge(new Vector2(target.xMin, target.yMax)) ||
               WithinEdge(new Vector2(target.xMax, target.yMin)) || WithinEdge(new Vector2(target.xMax, target.yMax));

        bool WithinEdge(Vector2 corner)
        {
            double x = (double)corner.x - origin.x, y = (double)corner.y - origin.y;
            double dot = direction.x * x + direction.y * y;
            double cosine = System.Math.Cos((DirectionEdgeToleranceDegrees + .0001d) * System.Math.PI / 180d);
            double directionLengthSquared = (double)direction.x * direction.x + (double)direction.y * direction.y;
            return dot >= 0d && dot * dot >= cosine * cosine * directionLengthSquared * (x * x + y * y);
        }
    }

    private static bool IntersectRayAxis(float origin, float direction, float min, float max, ref double near, ref double far)
    {
        if (direction == 0f) return origin >= min && origin <= max;
        double first = ((double)min - origin) / direction, last = ((double)max - origin) / direction;
        near = System.Math.Max(near, System.Math.Min(first, last));
        far = System.Math.Min(far, System.Math.Max(first, last));
        return near <= far;
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

    private bool TryGetVisibleRect(RectTransform rect, out Rect visible)
    {
        visible = default;
        if (!rect.gameObject.activeInHierarchy) return false;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled || !canvas.rootCanvas.isActiveAndEnabled) return false;
        foreach (CanvasGroup group in rect.GetComponentsInParent<CanvasGroup>())
            if (group.alpha <= .001f) return false;
        Camera camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        visible = Intersect(ScreenRect(rect, camera), new Rect(0f, 0f, Screen.width, Screen.height));
        foreach (RectMask2D mask in rect.GetComponentsInParent<RectMask2D>())
            if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
        foreach (Mask mask in rect.GetComponentsInParent<Mask>())
            if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
        return Finite(new Vector2(visible.xMin, visible.yMin)) && Finite(new Vector2(visible.xMax, visible.yMax)) &&
            visible.width > 0f && visible.height > 0f;
    }

    private bool TryGetVisibleBounds(Inventory inventory, RectTransform window, out Rect bounds)
    {
        bounds = default;
        bool found = false;
        if (inventory.itemSlot_UI == null) return false;
        for (int i = 0; i < inventory.itemSlot_UI.Count; i++)
        {
            ItemSlot_UI slot = inventory.itemSlot_UI[i];
            if (slot == null || slot.slotIndex != i || !slot.isActiveAndEnabled ||
                !ReferenceEquals(slot.GetBoundSlotData(), inventory.Data.GetItemSlot(i)) ||
                !slot.transform.IsChildOf(panel.transform) || slot.transform is not RectTransform rect) continue;
            if (window != null && !slot.transform.IsChildOf(window) || !TryGetVisibleRect(rect, out Rect visible)) continue;
            bounds = found ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, visible.xMin), Mathf.Min(bounds.yMin, visible.yMin),
                Mathf.Max(bounds.xMax, visible.xMax), Mathf.Max(bounds.yMax, visible.yMax)) : visible;
            found = true;
        }
        return found;
    }
    #endregion
}
