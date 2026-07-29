using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 为使用 UI_Bag 的库存面板提供一键整理入口。
/// 仅负责按钮创建和事件转发，排序规则由 Inventory_Data 持有。
/// </summary>
public sealed class InventorySortButton : MonoBehaviour
{
    private const string BagPanelName = "UI_Bag";
    private const string SortButtonName = "整理";

    private Inventory inventory;
    private Button button;

public static void EnsureFor(Inventory targetInventory)
    {
        if (targetInventory?.basePanel == null ||
            targetInventory.InventoryPanel_Prefab == null ||
            !string.Equals(targetInventory.InventoryPanel_Prefab.name, BagPanelName, StringComparison.Ordinal))
        {
            return;
        }

        Button sortButton = FindSortButton(targetInventory.basePanel.transform);
        if (sortButton == null)
        {
            Debug.LogError(
                "[InventorySortButton] UI_Bag Prefab 缺少“整理”按钮，请在 Prefab 中直接编辑。",
                targetInventory.basePanel);
            return;
        }

        InventorySortButton binder = sortButton.GetComponent<InventorySortButton>();
        if (binder == null)
            binder = sortButton.gameObject.AddComponent<InventorySortButton>();

        binder.Bind(targetInventory, sortButton);
    }

    private void Bind(Inventory targetInventory, Button targetButton)
    {
        inventory = targetInventory;
        button = targetButton;
        button.onClick.RemoveListener(HandleSort);
        button.onClick.AddListener(HandleSort);
    }

    private void HandleSort()
    {
        if (inventory?.Data == null || !inventory.Data.SortDefault())
            return;

        inventory.RefreshUI();
        if (inventory.item != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(inventory.item);
    }

    private static Button FindSortButton(Transform panel)
    {
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == SortButtonName)
                return buttons[i];
        }

        return null;
    }






}
