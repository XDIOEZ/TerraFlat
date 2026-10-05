using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 行囊搜索只控制槽位明暗，不改变库存数据、槽位顺序或交互。
/// 匹配来源为物品定义的中英文名称、稳定 ID，以及实例 Tag 的稳定 ID/多语言标签；未命中槽位透明度为 0.25。
/// </summary>
public sealed class InventoryBagSearch : MonoBehaviour
{
    #region 配置与状态

    private const float UnmatchedAlpha = 0.25f;
    private const string ClearButtonName = "清空搜索";

    [SerializeField] private TMP_InputField searchInput; // 标题栏中的共用输入框。

    private readonly Dictionary<ItemSlot_UI, CanvasGroup> slotGroups = new();
    private readonly Dictionary<RuntimeItemDefinition, string> englishNames = new();
    private Button clearButton; // 搜索框右侧的小扫把按钮。
    private Inventory inventory; // 当前面板绑定的真实库存。
    private string query = string.Empty; // 已去除首尾空格的搜索词。

    /// <summary>当前是否存在有效搜索词。</summary>
    public bool HasActiveQuery => query.Length > 0;

    #endregion

    #region 生命周期与绑定

    private void OnEnable()
    {
        clearButton = searchInput.transform.Find(ClearButtonName)?.GetComponent<Button>();
        searchInput.onValueChanged.AddListener(OnSearchTextChanged);
        if (clearButton != null)
            clearButton.onClick.AddListener(ClearSearch);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        OnSearchTextChanged(searchInput.text);
    }

    private void OnDisable()
    {
        searchInput.onValueChanged.RemoveListener(OnSearchTextChanged);
        if (clearButton != null)
            clearButton.onClick.RemoveListener(ClearSearch);
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void OnDestroy()
    {
        if (inventory?.Data != null)
            inventory.Data.Event_RefreshUI -= OnSlotRefreshed;
    }

    /// <summary>库存每次创建或扩容槽位后重新绑定表现与数据刷新事件。</summary>
    public void Bind(Inventory target)
    {
        if (inventory?.Data != null)
            inventory.Data.Event_RefreshUI -= OnSlotRefreshed;

        inventory = target;
        inventory.Data.Event_RefreshUI += OnSlotRefreshed;

        slotGroups.Clear();
        englishNames.Clear();
        OnSearchTextChanged(searchInput.text);
    }

    #endregion

    #region 搜索与显示

    /// <summary>输入变化时重新比较所有现存槽位。</summary>
    private void OnSearchTextChanged(string value)
    {
        query = value?.Trim() ?? string.Empty;
        if (clearButton != null)
            clearButton.interactable = !string.IsNullOrEmpty(value);
        RefreshAllSlots();
    }

    /// <summary>手机端无需重新呼出软键盘即可一键清空搜索。</summary>
    private void ClearSearch()
    {
        if (string.IsNullOrEmpty(searchInput.text))
            return;

        searchInput.text = string.Empty;
    }

    /// <summary>语言切换后刷新显示名对应的匹配结果。</summary>
    private void OnLanguageChanged(string _)
    {
        englishNames.Clear();
        RefreshAllSlots();
    }

    /// <summary>库存事务刷新一个槽位时，同步该槽的搜索明暗。</summary>
    private void OnSlotRefreshed(int index)
    {
        if (inventory?.Data?.itemSlots == null ||
            index < 0 || index >= inventory.itemSlot_UI.Count ||
            index >= inventory.Data.itemSlots.Count)
            return;

        ItemSlot_UI slot = inventory.itemSlot_UI[index];
        if (slot == null)
            return;

        RefreshSlotView(slot, index);
    }

    /// <summary>虚拟化网格滚动重绑后只刷新实际存在的槽位表现。</summary>
    public void RefreshRenderedSlots()
    {
        RefreshAllSlots();
    }

    /// <summary>虚拟化槽位发生单格数据刷新时只更新当前渲染格。</summary>
    public void RefreshRenderedSlot(ItemSlot_UI slot, int index)
    {
        RefreshSlotView(slot, index);
    }

    /// <summary>保留空格和拖拽目标，仅改变未命中槽位的视觉透明度。</summary>
    private void RefreshAllSlots()
    {
        if (inventory?.Data?.itemSlots == null)
            return;

        InventoryVirtualizedSlotGrid virtualizedGrid = inventory.basePanel != null
            ? inventory.basePanel.GetComponent<InventoryVirtualizedSlotGrid>()
            : null;
        if (virtualizedGrid != null)
        {
            foreach (ItemSlot_UI slot in virtualizedGrid.PooledSlots)
            {
                if (slot == null || !slot.gameObject.activeSelf)
                    continue;

                RefreshSlotView(slot, slot.slotIndex);
            }
            return;
        }

        for (int index = 0; index < inventory.itemSlot_UI.Count; index++)
            OnSlotRefreshed(index);
    }

    /// <summary>按当前绑定索引刷新单个渲染槽，CanvasGroup 只按实际池大小缓存。</summary>
    private void RefreshSlotView(ItemSlot_UI slot, int index)
    {
        if (slot == null || inventory?.Data?.itemSlots == null ||
            index < 0 || index >= inventory.Data.itemSlots.Count)
            return;

        if (!slotGroups.TryGetValue(slot, out CanvasGroup group))
        {
            group = slot.GetComponent<CanvasGroup>();
            if (group == null)
                group = slot.gameObject.AddComponent<CanvasGroup>();
            slotGroups[slot] = group;
        }

        ItemData item = inventory.Data.itemSlots[index]?.itemData;
        group.alpha = query.Length == 0 || Matches(item) ? 1f : UnmatchedAlpha;
    }

    /// <summary>中文、英文和标签均按不区分大小写的片段匹配。</summary>
    private bool Matches(ItemData item)
    {
        if (item == null)
            return false;

        if (Contains(item.IDName))
            return true;

        if (item.Tags != null)
        {
            foreach (string tag in item.Tags)
            {
                if (ItemTagLocalizationCatalog.MatchesSearch(tag, query))
                    return true;
            }
        }

        if (GameRes.Instance.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition))
        {
            if (Contains(definition.SourceDisplayName) ||
                Contains(definition.DisplayName) ||
                Contains(GetEnglishName(definition)))
                return true;
        }

        return false;
    }

    /// <summary>供排序复用与当前搜索完全一致的命中规则。</summary>
    public bool MatchesCurrentQuery(ItemData item)
    {
        return HasActiveQuery && Matches(item);
    }

    /// <summary>按运行时定义缓存英文译名，避免每次输入都重复读取本地化表。</summary>
    private string GetEnglishName(RuntimeItemDefinition definition)
    {
        if (englishNames.TryGetValue(definition, out string name))
            return name;

        name = FlatWorldLocalizationService.GetInLocale(
            definition.LabelKey,
            FlatWorldLocalizationService.FallbackLocaleCode,
            tableName: FlatWorldLocalizationService.DefaultTable);
        if (!string.IsNullOrEmpty(name))
            englishNames.Add(definition, name);
        return name;
    }

    /// <summary>允许输入名称或标签的一部分，无需完全相同。</summary>
    private bool Contains(string text)
    {
        return !string.IsNullOrEmpty(text) &&
               text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion
}
