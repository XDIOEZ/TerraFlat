using System;
using TMPro;
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
            sortButton = CreateSortButton(targetInventory.basePanel);

        if (sortButton == null)
        {
            Debug.LogError("[InventorySortButton] 无法创建背包整理按钮。");
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

    private static Button CreateSortButton(BasePanel panel)
    {
        GameObject buttonObject = null;
        GameObject buttonPrefab = GameRes.Instance != null ? GameRes.Instance.GetPrefab("Button") : null;
        if (buttonPrefab != null)
            buttonObject = Instantiate(buttonPrefab, panel.transform, false);

        if (buttonObject == null)
            buttonObject = CreateFallbackButton(panel.transform);

        buttonObject.name = SortButtonName;
        buttonObject.layer = panel.gameObject.layer;
        buttonObject.transform.SetAsLastSibling();

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        if (rect == null)
            rect = buttonObject.AddComponent<RectTransform>();

        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 10f);
        rect.sizeDelta = new Vector2(112f, 38f);
        rect.localScale = Vector3.one;

        LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        Button result = buttonObject.GetComponent<Button>();
        if (result == null)
            result = buttonObject.AddComponent<Button>();

        ConfigureVisuals(panel, result);
        Canvas.ForceUpdateCanvases();
        if (panel.rectTransform != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.rectTransform);

        return result;
    }

    private static GameObject CreateFallbackButton(Transform parent)
    {
        GameObject result = new GameObject(
            SortButtonName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        result.transform.SetParent(parent, false);

        GameObject labelObject = new GameObject(
            "Text (TMP)",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(result.transform, false);
        return result;
    }

    private static void ConfigureVisuals(BasePanel panel, Button targetButton)
    {
        Image image = targetButton.GetComponent<Image>();
        if (image == null)
            image = targetButton.gameObject.AddComponent<Image>();

        image.color = FlatWorldUITheme.SurfaceRaised;
        image.raycastTarget = true;
        targetButton.targetGraphic = image;

        Outline outline = targetButton.GetComponent<Outline>();
        if (outline == null)
            outline = targetButton.gameObject.AddComponent<Outline>();
        outline.effectColor = FlatWorldUITheme.Border;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        ColorBlock colors = targetButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = FlatWorldUITheme.AccentHover;
        colors.pressedColor = new Color(0.72f, 0.76f, 0.75f, 0.82f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.47f, 0.49f, 0.49f, 0.48f);
        colors.fadeDuration = 0.11f;
        targetButton.colors = colors;

        if (targetButton.GetComponent<FlatWorldUIFeedback>() == null)
            targetButton.gameObject.AddComponent<FlatWorldUIFeedback>();

        TMP_Text label = targetButton.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            GameObject labelObject = new GameObject(
                "Text (TMP)",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(targetButton.transform, false);
            label = labelObject.GetComponent<TextMeshProUGUI>();
        }

        TMP_Text referenceText = panel.GetComponentInChildren<TMP_Text>(true);
        if (referenceText != null && label.font == null)
            label.font = referenceText.font;

        label.text = SortButtonName;
        label.fontSize = 16f;
        label.fontStyle = FontStyles.Bold;
        label.color = FlatWorldUITheme.TextPrimary;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        labelRect.localScale = Vector3.one;
    }
}
