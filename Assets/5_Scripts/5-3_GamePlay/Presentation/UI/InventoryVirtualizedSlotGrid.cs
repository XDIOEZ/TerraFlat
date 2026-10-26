using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 行囊虚拟化网格：只保留可视区域附近的槽位对象，滚动时复用并重绑真实库存索引。
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryVirtualizedSlotGrid : MonoBehaviour, IScrollHandler
{
    #region 配置与状态

    [SerializeField, Min(0)] private int bufferRows = 1;
    private static readonly ProfilerMarker ResizeMarker = new("FlatWorld.Inventory.VirtualGrid.Resize");
    private static readonly ProfilerMarker RefreshMarker = new("FlatWorld.Inventory.VirtualGrid.Refresh");
    private static readonly ProfilerMarker RebindMarker = new("FlatWorld.Inventory.VirtualGrid.Rebind");
    private static readonly ProfilerMarker FilterMarker = new("FlatWorld.Inventory.VirtualGrid.Filter");

    private readonly List<ItemSlot_UI> pooledSlots = new();
    private readonly List<int> boundIndices = new();
    private readonly List<bool> initializedBindings = new();
    private readonly List<int> filteredIndices = new();
    private Predicate<ItemData> filter;
    private bool filterDirty;
    private bool resetScrollPending;

    private Inventory inventory;
    private RectTransform content;
    private RectTransform viewport;
    private ScrollRect scrollRect;
    private GridLayoutGroup gridLayout;
    private ContentSizeFitter contentSizeFitter;
    private InventorySlotVisualProfile visualProfile;
    private InventoryBagSearch bagSearch;
    private BasePanel ownerPanel;
    private GameObject slotPrefab;

    private Vector2 cellSize = new(80f, 80f);
    private Vector2 spacing = new(4f, 4f);
    private RectOffset padding;
    private int columns = 1;
    private int firstBoundRow = -1;
    private Vector2 lastViewportSize;
    private bool refreshPending;

    public IReadOnlyList<ItemSlot_UI> PooledSlots => pooledSlots;
    public bool IsBound => inventory != null;
    public int VisibleSlotCount => filter != null ? filteredIndices.Count : inventory?.Data?.itemSlots?.Count ?? 0;
    public event Action<int> VisibleCountChanged;

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

    /// <summary>解除虚拟化绑定并恢复固定网格布局，供生存背包使用。</summary>
    public void Unbind()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        if (ownerPanel != null)
            ownerPanel.Opened -= HandlePanelOpened;
        for (int i = 0; i < pooledSlots.Count; i++)
            if (boundIndices[i] >= 0 && pooledSlots[i] != null)
                inventory?.UnbindVirtualSlotUI(pooledSlots[i], boundIndices[i]);
        pooledSlots.Clear();
        boundIndices.Clear();
        initializedBindings.Clear();
        filteredIndices.Clear();
        filter = null;
        filterDirty = resetScrollPending = false;
        inventory = null;
        ownerPanel = null;
        refreshPending = false;
        if (gridLayout != null)
            gridLayout.enabled = true;
        if (contentSizeFitter != null)
            contentSizeFitter.enabled = true;
    }

    private void LateUpdate()
    {
        if (inventory == null || viewport == null)
            return;

        // 关闭面板仍保留组件运行时，跳过不可见槽位的布局和批量刷新，重新打开时补做一次。
        if (ownerPanel != null && !ownerPanel.IsOpen())
        {
            refreshPending = true;
            return;
        }

        Vector2 viewportSize = viewport.rect.size;
        if ((viewportSize - lastViewportSize).sqrMagnitude > 0.25f)
        {
            using var marker = ResizeMarker.Auto();
            lastViewportSize = viewportSize;
            EnsurePoolCapacity();
            UpdateContentSize();
            RebindVisibleSlots(true, refreshExisting: false);
        }

        if (refreshPending && !inventory.HasActiveSlotDrag)
        {
            using var marker = RefreshMarker.Auto();
            RebindVisibleSlots(true);
        }
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

        if (ownerPanel != null)
            ownerPanel.Opened -= HandlePanelOpened;
        inventory = targetInventory;
        ownerPanel = GetComponent<BasePanel>();
        if (ownerPanel != null)
            ownerPanel.Opened += HandlePanelOpened;
        filterDirty = true;
        content = targetContent;
        slotPrefab = targetSlotPrefab;
        visualProfile = targetVisualProfile;
        // 关闭的面板也会绑定库存，必须保留对停用父级滚动组件的引用。
        scrollRect = content.GetComponentInParent<ScrollRect>(true);
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
        initializedBindings.Clear();

        for (int i = 0; i < content.childCount; i++)
        {
            ItemSlot_UI slot = content.GetChild(i).GetComponent<ItemSlot_UI>();
            if (slot == null)
                continue;

            pooledSlots.Add(slot);
            boundIndices.Add(-1);
            initializedBindings.Add(false);
            PrepareSlotTransform(slot);
            visualProfile?.Apply(slot);
        }
    }

    #endregion

    #region 虚拟化布局

    private void OnScrollValueChanged(Vector2 _)
    {
        if (ownerPanel != null && !ownerPanel.IsOpen())
        {
            refreshPending = true;
            return;
        }
        RebindVisibleSlots(false);
    }

    /// <summary>重新打开时立即提交关闭期间累计的库存变化，不让旧槽位短暂出现在面板中。</summary>
    private void HandlePanelOpened()
    {
        RebindVisibleSlots(true);
    }

    /// <summary>行囊空白区域收到滚轮时同样交给内部滚动列表。</summary>
    public void OnScroll(PointerEventData eventData)
    {
        if (scrollRect == null || eventData == null)
            return;

        scrollRect.OnScroll(eventData);
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
            initializedBindings.Add(false);
            PrepareSlotTransform(slot);
            visualProfile?.Apply(slot);
        }
    }

    private void UpdateContentSize()
    {
        if (inventory?.Data?.itemSlots == null || content == null)
            return;

        int rowCount = Mathf.CeilToInt(VisibleSlotCount / (float)columns);
        float height = padding.top + padding.bottom;
        if (rowCount > 0)
            height += rowCount * cellSize.y + Mathf.Max(0, rowCount - 1) * spacing.y;

        Vector2 size = content.sizeDelta;
        float targetHeight = Mathf.Max(0f, height);
        if (!Mathf.Approximately(size.y, targetHeight))
        {
            size.y = targetHeight;
            content.sizeDelta = size;
        }
    }

    private void RebindVisibleSlots(bool force, bool refreshExisting = true)
    {
        using var marker = RebindMarker.Auto();
        if (inventory?.Data?.itemSlots == null || content == null)
            return;

        if (inventory.HasActiveSlotDrag)
        {
            refreshPending = true;
            return;
        }

        refreshPending = false;
        if (filterDirty)
        {
            RebuildFilteredIndices();
            force = true;
        }
        int rowCount = Mathf.CeilToInt(VisibleSlotCount / (float)columns);
        float rowStep = Mathf.Max(1f, cellSize.y + spacing.y);
        float scrollY = Mathf.Max(0f, content.anchoredPosition.y);
        int visibleRow = Mathf.Clamp(Mathf.FloorToInt(scrollY / rowStep), 0, Mathf.Max(0, rowCount - 1));
        int firstRow = Mathf.Max(0, visibleRow - bufferRows);

        if (!force && firstRow == firstBoundRow)
            return;

        firstBoundRow = firstRow;
        int firstIndex = firstRow * columns;
        int poolCount = pooledSlots.Count;
        if (poolCount == 0)
            return;

        // 数据索引固定映射到环形池，仍在可视范围的槽位不换对象、不重绑回调。
        int poolOffset = firstIndex % poolCount;
        // 投影可能把任意两格交换到同一池内，先解绑全部旧映射再绑定，避免覆盖别人的新映射。
        for (int poolIndex = 0; poolIndex < poolCount; poolIndex++)
        {
            int viewIndex = firstIndex + (poolIndex - poolOffset + poolCount) % poolCount;
            int nextIndex = ResolveDataIndex(viewIndex);
            int oldIndex = boundIndices[poolIndex];
            if (oldIndex >= 0 && oldIndex != nextIndex)
            {
                inventory.UnbindVirtualSlotUI(pooledSlots[poolIndex], oldIndex);
                boundIndices[poolIndex] = -1;
            }
        }
        for (int poolIndex = 0; poolIndex < pooledSlots.Count; poolIndex++)
        {
            ItemSlot_UI slot = pooledSlots[poolIndex];
            if (slot == null)
                continue;

            int viewIndex = firstIndex + (poolIndex - poolOffset + poolCount) % poolCount;
            int dataIndex = ResolveDataIndex(viewIndex);
            int oldIndex = boundIndices[poolIndex];
            if (oldIndex >= 0 && oldIndex != dataIndex)
                inventory.UnbindVirtualSlotUI(slot, oldIndex);

            if (dataIndex < 0 || dataIndex >= inventory.Data.itemSlots.Count)
            {
                boundIndices[poolIndex] = -1;
                if (slot.gameObject.activeSelf)
                    slot.gameObject.SetActive(false);
                continue;
            }

            if (!slot.gameObject.activeSelf)
                slot.gameObject.SetActive(true);
            if (oldIndex != dataIndex)
            {
                PositionSlot(slot, viewIndex);
                if (initializedBindings[poolIndex])
                    inventory.RebindVirtualSlotUI(slot, dataIndex);
                else
                {
                    inventory.BindSlotUI(slot, dataIndex);
                    initializedBindings[poolIndex] = true;
                }
                boundIndices[poolIndex] = dataIndex;
                bagSearch?.RefreshRenderedSlot(slot, dataIndex);
            }
            else if (force && refreshExisting)
            {
                PositionSlot(slot, viewIndex);
                slot.RefreshUI();
                bagSearch?.RefreshRenderedSlot(slot, dataIndex);
            }
        }
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

    /// <summary>筛选只保存显示索引映射，不整理、复制或删除库存物品。</summary>
    public void SetFilter(Predicate<ItemData> predicate, bool resetScroll = true)
    {
        filter = predicate;
        filterDirty = true;
        resetScrollPending |= resetScroll;
        if (ownerPanel != null && !ownerPanel.IsOpen())
        {
            refreshPending = true;
            return;
        }
        RebindVisibleSlots(true);
    }

    private int ResolveDataIndex(int viewIndex)
    {
        if (viewIndex < 0 || viewIndex >= VisibleSlotCount) return -1;
        return filter != null ? filteredIndices[viewIndex] : viewIndex;
    }

    private void RebuildFilteredIndices()
    {
        using var marker = FilterMarker.Auto();
        filteredIndices.Clear();
        if (filter != null)
            for (int index = 0; index < inventory.Data.itemSlots.Count; index++)
                if (filter(inventory.Data.itemSlots[index]?.itemData)) filteredIndices.Add(index);
        filterDirty = false;
        UpdateContentSize();
        if (scrollRect != null && resetScrollPending) scrollRect.StopMovement();
        float maxY = Mathf.Max(0f, content.rect.height - (viewport != null ? viewport.rect.height : 0f));
        Vector2 position = content.anchoredPosition;
        position.y = resetScrollPending ? 0f : Mathf.Clamp(position.y, 0f, maxY);
        content.anchoredPosition = position;
        resetScrollPending = false;
        firstBoundRow = -1;
        VisibleCountChanged?.Invoke(VisibleSlotCount);
    }

    /// <summary>刷新当前已经实例化的槽位，不遍历整份库存数据。</summary>
    public void RefreshVisibleSlots()
    {
        filterDirty |= filter != null;
        if (ownerPanel != null && !ownerPanel.IsOpen())
        {
            refreshPending = true;
            return;
        }
        RebindVisibleSlots(true);
    }

    /// <summary>仅刷新当前可见的数据索引；不可见索引无需创建 UI。</summary>
    public void RefreshSlot(int dataIndex)
    {
        if (ownerPanel != null && !ownerPanel.IsOpen())
        {
            filterDirty |= filter != null;
            refreshPending = true;
            return;
        }
        // 单槽物品变化未改变筛选命中关系时，只刷新本槽，不重新过滤整个库存。
        if (filter != null)
        {
            bool membershipChanged = filterDirty || inventory?.Data?.itemSlots == null ||
                dataIndex < 0 || dataIndex >= inventory.Data.itemSlots.Count ||
                (filteredIndices.BinarySearch(dataIndex) >= 0) != filter(inventory.Data.itemSlots[dataIndex]?.itemData);
            if (membershipChanged)
            {
                filterDirty = true;
                refreshPending = true;
            }
        }
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
