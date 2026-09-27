using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 将 UI_Bag 的排序和整理操作绑定到各自按钮。
/// 排序按稳定 ID、分类、堆叠数量、重量、体积循环；搜索激活时命中项优先；整理只合并堆叠并压紧空槽。
/// </summary>
public sealed class InventorySortButton : MonoBehaviour
{
    #region 常量与状态

    private const string BagPanelName = "UI_Bag";
    private const string SortButtonName = "排序";
    private const string OrganizeButtonName = "整理";

    private static readonly InventorySortMode[] SortModes =
    {
        InventorySortMode.Id,
        InventorySortMode.Category,
        InventorySortMode.AmountDescending,
        InventorySortMode.WeightDescending,
        InventorySortMode.VolumeDescending
    };

    private Inventory inventory;
    private InventoryBagSearch bagSearch;
    private Button sortButton;
    private Button organizeButton;
    private TMP_Text sortButtonLabel;
    private int currentSortModeIndex;
    private int nextSortModeIndex;

    #endregion

    #region 绑定

    /// <summary>为使用 UI_Bag 的库存面板接入独立排序与整理按钮。</summary>
    public static void EnsureFor(Inventory targetInventory)
    {
        if (targetInventory?.basePanel == null ||
            targetInventory.InventoryPanel_Prefab == null ||
            !string.Equals(targetInventory.InventoryPanel_Prefab.name, BagPanelName, StringComparison.Ordinal))
        {
            return;
        }

        Transform panel = targetInventory.basePanel.transform;
        Button sortButton = FindButton(panel, SortButtonName);
        Button organizeButton = FindButton(panel, OrganizeButtonName);
        if (sortButton == null || organizeButton == null)
        {
            Debug.LogError(
                "[InventorySortButton] UI_Bag Prefab 缺少“排序”或“整理”按钮，请检查 Prefab。",
                targetInventory.basePanel);
            return;
        }

        InventorySortButton binder = organizeButton.GetComponent<InventorySortButton>();
        if (binder == null)
            binder = organizeButton.gameObject.AddComponent<InventorySortButton>();

        binder.Bind(targetInventory, sortButton, organizeButton);
    }

    /// <summary>移除旧监听后重绑按钮，避免面板复用时重复执行库存操作。</summary>
    private void Bind(Inventory targetInventory, Button targetSortButton, Button targetOrganizeButton)
    {
        if (!ReferenceEquals(inventory, targetInventory))
        {
            currentSortModeIndex = 0;
            nextSortModeIndex = 0;
        }

        sortButton?.onClick.RemoveListener(HandleSort);
        organizeButton?.onClick.RemoveListener(HandleOrganize);

        inventory = targetInventory;
        bagSearch = targetInventory.basePanel.GetComponent<InventoryBagSearch>();
        sortButton = targetSortButton;
        organizeButton = targetOrganizeButton;
        sortButtonLabel = sortButton.GetComponentInChildren<TMP_Text>(true);

        sortButton.onClick.RemoveListener(HandleSort);
        sortButton.onClick.AddListener(HandleSort);
        organizeButton.onClick.RemoveListener(HandleOrganize);
        organizeButton.onClick.AddListener(HandleOrganize);

        RefreshSortButtonLabel(SortModes[currentSortModeIndex]);
    }

    /// <summary>按名称查找面板内的按钮。</summary>
    private static Button FindButton(Transform panel, string buttonName)
    {
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && string.Equals(buttons[i].name, buttonName, StringComparison.Ordinal))
                return buttons[i];
        }

        return null;
    }

    #endregion

    #region 排序与整理

    /// <summary>执行当前排序规则并推进循环，即使本次库存无需移动也响应一次点击。</summary>
    private void HandleSort()
    {
        if (inventory?.Data == null)
            return;

        InventorySortMode mode = SortModes[nextSortModeIndex];
        currentSortModeIndex = nextSortModeIndex;
        nextSortModeIndex = (nextSortModeIndex + 1) % SortModes.Length;

        bool changed = bagSearch != null && bagSearch.HasActiveQuery
            ? inventory.Data.Sort(mode, bagSearch.MatchesCurrentQuery)
            : inventory.Data.Sort(mode);
        if (changed)
            RefreshInventory();

        RefreshSortButtonLabel(mode);
    }

    /// <summary>让按钮括号始终显示最近一次使用的排序规则。</summary>
    private void RefreshSortButtonLabel(InventorySortMode mode)
    {
        if (sortButtonLabel == null)
            return;

        string sortText = FlatWorldLocalizationService.GetUiText("排序");
        string modeText = FlatWorldLocalizationService.GetUiText(GetSortModeName(mode));
        sortButtonLabel.text = $"{sortText}（{modeText}）";
    }

    /// <summary>返回排序规则的玩家可见名称。</summary>
    private static string GetSortModeName(InventorySortMode mode)
    {
        return mode switch
        {
            InventorySortMode.Id => "ID",
            InventorySortMode.Category => "分类",
            InventorySortMode.AmountDescending => "数量",
            InventorySortMode.WeightDescending => "重量",
            InventorySortMode.VolumeDescending => "体积",
            _ => "ID"
        };
    }

    /// <summary>只合并可堆叠物品并压紧空槽，不选择新的排序规则。</summary>
    private void HandleOrganize()
    {
        if (inventory?.Data == null || !inventory.Data.Organize())
            return;

        RefreshInventory();
    }

    /// <summary>刷新库存界面并同步容器运行态。</summary>
    private void RefreshInventory()
    {
        inventory.RefreshUI();
        if (inventory.item != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(inventory.item);
    }

    #endregion
}
