using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 行囊虚拟化网格：只保留可视区域附近的槽位对象，滚动时复用并重绑真实库存索引。
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryVirtualizedSlotGrid : MonoBehaviour
{
    #region 配置与状态

    [SerializeField, Min(0)] private int bufferRows = 1;

    private readonly List<ItemSlot_UI> pooledSlots = new();
    private readonly List<int> boundIndices = new();

    private Inventory inventory;
    private RectTransform content;
    private RectTransform viewport;
    private ScrollRect scrollRect;
    private GridLayoutGroup gridLayout;
    private ContentSizeFitter contentSizeFitter;
    private InventorySlotVisualProfile visualProfile;
    private InventoryBagSearch bagSearch;
    private GameObject slotPrefab;

    private Vector2 cellSize = new(80f, 80f);
    private Vector2 spacing = new(4f, 4f);
    private RectOffset padding;
    private int columns = 1;
    private int firstBoundRow = -1;
    private Vector2 lastViewportSize;
    private bool refreshPending;

    public IReadOnlyList<ItemSlot_UI> PooledSlots => pooledSlots;

    #endregion

    #region 生命周期与绑定

    private void OnEnable()
    {
        if (scrollRect == null)
            return;

        scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        refreshPending = true;
    }

    private void OnDisable()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
    }

    private void LateUpdate()
    {
        if (inventory == null || viewport == null)
            return;

        Vector2 viewportSize = viewport.rect.size;
        if ((viewportSize - lastViewportSize).sqrMagnitude > 0.25f)
        {
            lastViewportSize = viewportSize;
            EnsurePoolCapacity();
            UpdateContentSize();
            RebindVisibleSlots(true);
        }

        if (refreshPending && !inventory.HasActiveSlotDrag)
            RebindVisibleSlots(true);
    }

    /// <summary>绑定真实库存，并把原有静态槽位转为固定大小的复用池。</summary>
    public void Bind(
        Inventory targetInventory,
        Transform contentTransform,
        GameObject targetSlotPrefab,
        InventorySlotVisualProfile targetVisualProfile)
    {
        if (targetInventory == null || contentTransform is not RectTransform targetContent || targetSlotPrefab == null)
            return;

        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);

        inventory = targetInventory;
        content = targetContent;
        slotPrefab = targetSlotPrefab;
        visualProfile = targetVisualProfile;
        scrollRect = content.GetComponentInParent<ScrollRect>();
        viewport = scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport
            : content.parent as RectTransform;
        bagSearch = GetComponent<InventoryBagSearch>();

        CacheGridSettings();
        CollectExistingSlots();
        inventory.PrepareVirtualizedSlotUIMap(inventory.Data?.itemSlots?.Count ?? 0);

        Canvas.ForceUpdateCanvases();
        lastViewportSize = viewport != null ? viewport.rect.size : Vector2.zero;
        EnsurePoolCapacity();
        UpdateContentSize();
        firstBoundRow = -1;
        RebindVisibleSlots(true);

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }
    }

    private void CacheGridSettings()
    {
        padding ??= new RectOffset(8, 8, 8, 8);
        gridLayout = content.GetComponent<GridLayoutGroup>();
        if (gridLayout != null)
        {
            cellSize = gridLayout.cellSize;
            spacing = gridLayout.spacing;
            padding = new RectOffset(
                gridLayout.padding.left,
                gridLayout.padding.right,
                gridLayout.padding.top,
                gridLayout.padding.bottom);
            columns = gridLayout.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                ? Mathf.Max(1, gridLayout.constraintCount)
                : Mathf.Max(1, Mathf.FloorToInt((content.rect.width - padding.horizontal + spacing.x) /
                                                Mathf.Max(1f, cellSize.x + spacing.x)));
            gridLayout.enabled = false;
        }

        contentSizeFitter = content.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null)
            contentSizeFitter.enabled = false;

        // 虚拟列表由自身维护总高度，槽位只按真实数据行定位。
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
    }

    private void CollectExistingSlots()
    {
        pooledSlots.Clear();
        boundIndices.Clear();

        for (int i = 0; i < content.childCount; i++)
        {
            ItemSlot_UI slot = content.GetChild(i).GetComponent<ItemSlot_UI>();
            if (slot == null)
                continue;

            pooledSlots.Add(slot);
            boundIndices.Add(-1);
            PrepareSlotTransform(slot);
            visualProfile?.Apply(slot);
        }
    }

    #endregion

    #region 虚拟化布局

    private void OnScrollValueChanged(Vector2 _)
    {
        RebindVisibleSlots(false);
    }

    private void EnsurePoolCapacity()
    {
        if (inventory?.Data?.itemSlots == null || viewport == null)
            return;

        float rowStep = Mathf.Max(1f, cellSize.y + spacing.y);
        int visibleRows = Mathf.Max(1, Mathf.CeilToInt(viewport.rect.height / rowStep));
        int totalRows = Mathf.CeilToInt(inventory.Data.itemSlots.Count / (float)columns);
        int desiredRows = Mathf.Min(totalRows, visibleRows + bufferRows * 2);
        int desiredCount = Mathf.Min(inventory.Data.itemSlots.Count, desiredRows * columns);

        while (pooledSlots.Count < desiredCount)
        {
            GameObject slotObject = Instantiate(slotPrefab, content, false);
            ItemSlot_UI slot = slotObject.GetComponent<ItemSlot_UI>();
            if (slot == null)
            {
                Destroy(slotObject);
                break;
            }

            pooledSlots.Add(slot);
            boundIndices.Add(-1);
            PrepareSlotTransform(slot);
            visualProfile?.Apply(slot);
        }
    }

    private void UpdateContentSize()
    {
        if (inventory?.Data?.itemSlots == null || content == null)
            return;

        int rowCount = Mathf.CeilToInt(inventory.Data.itemSlots.Count / (float)columns);
        float height = padding.top + padding.bottom;
        if (rowCount > 0)
            height += rowCount * cellSize.y + Mathf.Max(0, rowCount - 1) * spacing.y;

        Vector2 size = content.sizeDelta;
        size.y = Mathf.Max(0f, height);
        content.sizeDelta = size;
    }

    private void RebindVisibleSlots(bool force)
    {
        if (inventory?.Data?.itemSlots == null || content == null)
            return;

        if (inventory.HasActiveSlotDrag)
        {
            refreshPending = true;
            return;
        }

        refreshPending = false;
        int rowCount = Mathf.CeilToInt(inventory.Data.itemSlots.Count / (float)columns);
        float rowStep = Mathf.Max(1f, cellSize.y + spacing.y);
        float scrollY = Mathf.Max(0f, content.anchoredPosition.y);
        int visibleRow = Mathf.Clamp(Mathf.FloorToInt(scrollY / rowStep), 0, Mathf.Max(0, rowCount - 1));
        int firstRow = Mathf.Max(0, visibleRow - bufferRows);

        if (!force && firstRow == firstBoundRow)
            return;

        firstBoundRow = firstRow;
        int firstIndex = firstRow * columns;
        for (int poolIndex = 0; poolIndex < pooledSlots.Count; poolIndex++)
        {
            ItemSlot_UI slot = pooledSlots[poolIndex];
            if (slot == null)
                continue;

            int dataIndex = firstIndex + poolIndex;
            int oldIndex = boundIndices[poolIndex];
            if (oldIndex >= 0 && oldIndex != dataIndex)
                inventory.UnbindVirtualSlotUI(slot, oldIndex);

            if (dataIndex < 0 || dataIndex >= inventory.Data.itemSlots.Count)
            {
                boundIndices[poolIndex] = -1;
                slot.gameObject.SetActive(false);
                continue;
            }

            PositionSlot(slot, dataIndex);
            slot.gameObject.SetActive(true);
            if (oldIndex != dataIndex)
            {
                inventory.BindSlotUI(slot, dataIndex);
                boundIndices[poolIndex] = dataIndex;
            }
            else if (force)
            {
                slot.RefreshUI();
            }
        }

        bagSearch?.RefreshRenderedSlots();
    }

    private void PrepareSlotTransform(ItemSlot_UI slot)
    {
        if (slot.transform is not RectTransform rect)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = cellSize;
    }

    private void PositionSlot(ItemSlot_UI slot, int dataIndex)
    {
        if (slot.transform is not RectTransform rect)
            return;

        int row = dataIndex / columns;
        int column = dataIndex % columns;
        rect.anchoredPosition = new Vector2(
            padding.left + column * (cellSize.x + spacing.x),
            -padding.top - row * (cellSize.y + spacing.y));
    }

    #endregion

    #region 外部刷新

    /// <summary>刷新当前已经实例化的槽位，不遍历整份库存数据。</summary>
    public void RefreshVisibleSlots()
    {
        RebindVisibleSlots(true);
    }

    /// <summary>仅刷新当前可见的数据索引；不可见索引无需创建 UI。</summary>
    public void RefreshSlot(int dataIndex)
    {
        if (inventory?.itemSlot_UI == null || dataIndex < 0 || dataIndex >= inventory.itemSlot_UI.Count)
            return;

        ItemSlot_UI slot = inventory.itemSlot_UI[dataIndex];
        if (slot == null)
            return;

        slot.RefreshUI();
        bagSearch?.RefreshRenderedSlot(slot, dataIndex);
    }

    #endregion
}
