using FlatWorld.Gameplay.InventoryFeedback;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>从真实源槽飞向实际收货槽的临时图标，不接管物品或输入。</summary>
public sealed class InventoryQuickTransferAnimation : MonoBehaviour
{
    #region 创建与可见性
    public const float Duration = 0.24f;
    private Endpoint source, target;
    private const int MaximumFlights = 32;
    private static readonly HashSet<InventoryQuickTransferAnimation> activeFlights = new();
    private RectTransform canvasRect, flight;
    private CanvasGroup opacity;
    private Camera eventCamera;
    private Vector2 start;
    private float elapsed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => activeFlights.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        InventoryQuickTransferFeedbackEvents.Committed -= Play;
        InventoryQuickTransferFeedbackEvents.Committed += Play;
    }

    private sealed class Endpoint
    {
        public Inventory Inventory;
        public int Index;
        public ItemSlot_UI Slot;
        public RectTransform Rect;
        public Transform Parent;
        public Canvas Canvas, Root;
        public Camera Camera;
        public CanvasGroup[] Groups;
        public RectMask2D[] RectMasks;
        public Mask[] Masks;

        public bool IsVisible()
        {
            if (Inventory?.itemSlot_UI == null || (uint)Index >= (uint)Inventory.itemSlot_UI.Count ||
                Inventory.itemSlot_UI[Index] != Slot || Slot == null || !Slot.isActiveAndEnabled ||
                Slot.slotIndex != Index || Rect == null || Rect.parent != Parent || Canvas == null ||
                !Canvas.isActiveAndEnabled || Root == null || !Root.isActiveAndEnabled) return false;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(Camera, Rect.TransformPoint(Rect.rect.center));
            if (center.x < 0f || center.y < 0f || center.x > Screen.width || center.y > Screen.height) return false;
            foreach (CanvasGroup group in Groups)
                if (group != null && group.alpha <= 0.001f) return false;
            foreach (RectMask2D mask in RectMasks)
                if (mask != null && mask.isActiveAndEnabled && !RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, center, Camera)) return false;
            foreach (Mask mask in Masks)
                if (mask != null && mask.isActiveAndEnabled && !RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, center, Camera)) return false;
            return true;
        }
    }

    private static Endpoint ResolveVisibleSlot(Inventory inventory, int index)
    {
        if (inventory?.itemSlot_UI == null || (uint)index >= (uint)inventory.itemSlot_UI.Count) return null;
        ItemSlot_UI slot = inventory.itemSlot_UI[index];
        if (slot == null || slot.slotIndex != index || !slot.isActiveAndEnabled || slot.image == null) return null;
        RectTransform rect = slot.transform as RectTransform;
        Canvas canvas = slot.GetComponentInParent<Canvas>();
        if (rect == null || canvas == null || !canvas.isActiveAndEnabled) return null;
        var endpoint = new Endpoint { Inventory = inventory, Index = index, Slot = slot, Rect = rect,
            Parent = rect.parent, Canvas = canvas, Root = canvas.rootCanvas,
            Camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera,
            Groups = slot.GetComponentsInParent<CanvasGroup>(), RectMasks = slot.GetComponentsInParent<RectMask2D>(),
            Masks = slot.GetComponentsInParent<Mask>() };
        return endpoint.IsVisible() ? endpoint : null;
    }

    private static void Play(InventoryQuickTransferSignal signal)
    {
        if (!signal.AllowLocalPresentation || activeFlights.Count >= MaximumFlights) return;
        Endpoint from = ResolveVisibleSlot(signal.Source, signal.SourceIndex);
        Endpoint to = ResolveVisibleSlot(signal.Target, signal.TargetIndex);
        if (from == null || to == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemPresentation(signal.Item, out _, out Sprite sprite) || sprite == null) return;
        Canvas root = from.Root;
        if (root.transform is not RectTransform rootRect) return;

        // 临时顶层 Canvas 不带射线组件，按真实格子中心跨窗口飞行。
        var go = new GameObject("InventoryQuickTransferFlight", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(root.transform, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = root.sortingLayerID;
        canvas.sortingOrder = UIManager.HeldItemSortingOrder;
        Image icon = go.GetComponent<Image>();
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var view = go.AddComponent<InventoryQuickTransferAnimation>();
        view.source = from; view.target = to;
        view.canvasRect = rootRect; view.flight = go.GetComponent<RectTransform>();
        view.eventCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        view.opacity = go.GetComponent<CanvasGroup>();
        view.opacity.blocksRaycasts = false; view.opacity.interactable = false;
        view.flight.anchorMin = view.flight.anchorMax = rootRect.pivot;
        view.flight.pivot = new Vector2(0.5f, 0.5f);
        var corners = new Vector3[4];
        from.Slot.image.rectTransform.GetWorldCorners(corners);
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(from.Camera, corner);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, view.eventCamera, out Vector2 point);
            minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point);
        }
        view.flight.sizeDelta = maximum - minimum;
        view.start = view.LocalCenter(from);
        view.flight.anchoredPosition = view.start;
        activeFlights.Add(view);
        if (from.Slot.text != null)
        {
            TMP_Text amount = Instantiate(from.Slot.text, view.flight);
            amount.gameObject.SetActive(true); amount.enabled = true; amount.raycastTarget = false;
            amount.text = signal.Amount.ToString("0.##");
        }
    }
    #endregion

    #region 飞行与清理
    private Vector2 LocalCenter(Endpoint endpoint)
    {
        RectTransform rect = endpoint.Rect;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(endpoint.Camera, rect.TransformPoint(rect.rect.center));
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, eventCamera, out Vector2 local);
        return local;
    }

    private void Update()
    {
        // 关闭面板、卸载库存或虚拟化重绑任一端时立即清掉旧动画。
        if (source == null || target == null || !source.IsVisible() || !target.IsVisible())
        { Destroy(gameObject); return; }
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / Duration);
        float progress = 1f - Mathf.Pow(1f - t, 3f);
        Vector2 end = LocalCenter(target);
        float arc = Mathf.Min(48f, Vector2.Distance(start, end) * 0.12f);
        flight.anchoredPosition = Vector2.Lerp(start, end, progress) + Vector2.up * (Mathf.Sin(progress * Mathf.PI) * arc);
        flight.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, progress);
        opacity.alpha = 1f - Mathf.InverseLerp(0.8f, 1f, t);
        if (t >= 1f) Destroy(gameObject);
    }

    private void OnDisable() => Destroy(gameObject);
    private void OnDestroy() => activeFlights.Remove(this);
    #endregion
}
