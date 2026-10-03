using System.Collections;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>库存物品的统一鼠标悬浮信息面板，只负责汇总定义、实例参数和模块返回文本。</summary>
[DisallowMultipleComponent]
public sealed class InventoryItemTooltip : MonoBehaviour
{
    #region 配置

    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image background;
    [SerializeField] private Outline outline;
    [SerializeField] private Image accent;
    [SerializeField, Min(240f)] private float panelWidth = 360f;
    [SerializeField, Min(0f)] private float contentPadding = 14f;
    [SerializeField, Min(0f)] private float titleHeight = 30f;
    [SerializeField, Min(0f)] private float sectionGap = 8f;
    [SerializeField, Min(0f)] private float pointerOffset = 18f;

    #endregion

    #region 运行时

    private static InventoryItemTooltip current;
    private ItemSlot_UI owner;
    private Vector2 pointerScreenPosition;
    private Coroutine refreshRoutine;
    private bool visible;

    public static void Show(ItemSlot_UI slot, GameObject prefab, Vector2 screenPosition)
    {
        if (slot == null || prefab == null || slot.GetBoundSlotData()?.itemData == null)
        {
            Hide(slot);
            return;
        }

        InventoryItemTooltip tooltip = EnsureInstance(prefab);
        if (tooltip == null)
            return;

        tooltip.owner = slot;
        tooltip.pointerScreenPosition = screenPosition;
        tooltip.visible = true;
        tooltip.canvasGroup.alpha = 1f;
        tooltip.canvasGroup.interactable = false;
        tooltip.canvasGroup.blocksRaycasts = false;
        tooltip.transform.SetAsLastSibling();
        tooltip.RefreshContent();
        tooltip.PositionPanel();
        tooltip.RestartRefreshRoutine();
    }

    public static void Move(ItemSlot_UI slot, Vector2 screenPosition)
    {
        if (current == null || !current.visible || current.owner != slot)
            return;

        current.pointerScreenPosition = screenPosition;
        current.PositionPanel();
    }

    public static void Refresh(ItemSlot_UI slot)
    {
        if (current == null || !current.visible || current.owner != slot)
            return;

        current.RefreshContent();
        current.PositionPanel();
    }

    public static void Hide(ItemSlot_UI slot)
    {
        if (current == null || (slot != null && current.owner != slot))
            return;

        current.HideInternal();
    }

    private static InventoryItemTooltip EnsureInstance(GameObject prefab)
    {
        if (current != null)
            return current;

        Canvas rootCanvas = UIManager.Instance?.RootCanvas;
        if (rootCanvas == null)
            return null;

        GameObject instance = Instantiate(prefab, rootCanvas.transform, false);
        instance.name = prefab.name;
        current = instance.GetComponent<InventoryItemTooltip>();
        if (current == null)
        {
            Debug.LogError("[InventoryItemTooltip] UI_ItemTooltip.prefab 缺少 InventoryItemTooltip 组件。", instance);
            Destroy(instance);
            return null;
        }

        current.EnsureReferences();
        current.HideInternal();
        return current;
    }

    private void Awake()
    {
        EnsureReferences();
        ApplyTheme();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;
    }

    private void EnsureReferences()
    {
        panelRect ??= transform as RectTransform;
        canvasGroup ??= GetComponent<CanvasGroup>();
        if (panelRect == null || titleText == null || bodyText == null || canvasGroup == null)
            Debug.LogError("[InventoryItemTooltip] 正式 Prefab 引用不完整。", this);
    }

    /// <summary>悬浮面板只从全局 UI 主题读取外观参数，避免形成第二套库存视觉规范。</summary>
    private void ApplyTheme()
    {
        if (background != null)
            background.color = FlatWorldUITheme.SurfaceLow;
        if (outline != null)
        {
            outline.effectColor = FlatWorldUITheme.Border;
            outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        }
        if (accent != null)
            accent.color = Color.white;
        if (titleText != null)
            titleText.color = FlatWorldUITheme.TextPrimary;
        if (bodyText != null)
            bodyText.color = FlatWorldUITheme.TextSecondary;
    }

    private void RestartRefreshRoutine()
    {
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);
        refreshRoutine = StartCoroutine(RefreshWhileVisible());
    }

    private IEnumerator RefreshWhileVisible()
    {
        while (visible)
        {
            yield return new WaitForSeconds(1f);
            if (!visible)
                break;
            RefreshContent();
            PositionPanel();
        }

        refreshRoutine = null;
    }

    private void HideInternal()
    {
        visible = false;
        owner = null;
        if (refreshRoutine != null)
        {
            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    #endregion

    #region 内容

    private void RefreshContent()
    {
        ItemData itemData = owner?.GetBoundSlotData()?.itemData;
        if (itemData == null)
        {
            HideInternal();
            return;
        }

        RuntimeItemDefinition definition = null;
        GameRes resources = GameRes.ExistingInstance;
        resources?.TryGetItemDefinition(itemData.IDName, out definition);

        titleText.text = definition?.DisplayName ??
            (!string.IsNullOrWhiteSpace(itemData.GameName) ? itemData.GameName : itemData.IDName);

        var parameters = new List<string>(8);
        AppendBaseParameters(itemData, parameters);
        AppendModuleParameters(resources, itemData, definition, parameters);

        string description = definition?.Description?.Trim() ?? string.Empty;
        string parameterText = string.Join("\n", parameters);
        bodyText.text = string.IsNullOrEmpty(description)
            ? parameterText
            : string.IsNullOrEmpty(parameterText)
                ? description
                : description + "\n\n" + parameterText;

        RebuildLayout();
    }

    private static void AppendBaseParameters(ItemData itemData, ICollection<string> lines)
    {
        ItemStack stack = itemData.Stack;
        if (stack != null)
        {
            lines.Add(FlatWorldLocalizationService.GetUiFormat("数量：{0}", FormatNumber(stack.Amount)));
            lines.Add(FlatWorldLocalizationService.GetUiFormat("重量：{0} kg", FormatNumber(stack.CurrentWeight)));
            lines.Add(FlatWorldLocalizationService.GetUiFormat("体积：{0} L", FormatNumber(stack.CurrentVolume)));
        }

        if (itemData.MaxDurability > 0f &&
            (itemData.MaxDurability > 1.001f || itemData.Durability < itemData.MaxDurability - 0.001f))
        {
            lines.Add(FlatWorldLocalizationService.GetUiFormat(
                "耐久：{0} / {1}",
                FormatNumber(itemData.Durability),
                FormatNumber(itemData.MaxDurability)));
        }

        if (itemData.MatterState?.Initialized == true)
        {
            lines.Add(FlatWorldLocalizationService.GetUiFormat(
                "温度：{0}℃",
                itemData.MatterState.TemperatureCelsius.ToString("0.#")));
        }
    }

    private static void AppendModuleParameters(
        GameRes resources,
        ItemData itemData,
        RuntimeItemDefinition definition,
        ICollection<string> lines)
    {
        if (resources == null || definition?.ModuleDefinitions == null)
            return;

        foreach (RuntimeItemModuleDefinition moduleDefinition in definition.ModuleDefinitions)
        {
            if (moduleDefinition == null || !moduleDefinition.Enabled)
                continue;

            string prefabId = !string.IsNullOrWhiteSpace(moduleDefinition.PrefabId)
                ? moduleDefinition.PrefabId
                : moduleDefinition.ModuleId;
            if (string.IsNullOrWhiteSpace(prefabId))
                continue;

            GameObject modulePrefab = resources.GetPrefab(prefabId, false);
            if (modulePrefab == null)
                continue;

            ModuleData moduleData = null;
            itemData.ModuleDataDic?.TryGetValue(moduleDefinition.StableName, out moduleData);
            if (moduleData != null && !moduleData.Enabled)
                continue;

            MonoBehaviour[] behaviours = modulePrefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (!(behaviour is IItemTooltipInfoProvider provider))
                    continue;

                var context = new ItemTooltipInfoContext(
                    itemData,
                    definition,
                    moduleDefinition,
                    moduleData);
                if (provider.TryGetItemTooltipInfo(context, out string info) &&
                    !string.IsNullOrWhiteSpace(info))
                {
                    lines.Add(info.Trim());
                }
            }
        }
    }

    private static string FormatNumber(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString("0.###");
    }

    private void RebuildLayout()
    {
        if (panelRect == null || titleText == null || bodyText == null)
            return;

        float innerWidth = Mathf.Max(1f, panelWidth - contentPadding * 2f);
        float bodyHeight = Mathf.Max(
            22f,
            bodyText.GetPreferredValues(bodyText.text, innerWidth, 0f).y);
        float panelHeight = contentPadding + titleHeight + sectionGap + bodyHeight + contentPadding;

        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);
        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);

        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(contentPadding, -contentPadding);
        titleRect.sizeDelta = new Vector2(innerWidth, titleHeight);

        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(0f, 1f);
        bodyRect.pivot = new Vector2(0f, 1f);
        bodyRect.anchoredPosition = new Vector2(
            contentPadding,
            -(contentPadding + titleHeight + sectionGap));
        bodyRect.sizeDelta = new Vector2(innerWidth, bodyHeight);
    }

    #endregion

    #region 定位

    private void PositionPanel()
    {
        if (!visible || panelRect == null)
            return;

        Canvas rootCanvas = UIManager.Instance?.RootCanvas;
        RectTransform rootRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;
        if (rootRect == null)
            return;

        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect,
                pointerScreenPosition,
                camera,
                out Vector2 localPoint))
        {
            return;
        }

        float width = panelRect.rect.width;
        float height = panelRect.rect.height;
        Rect bounds = rootRect.rect;

        float x = localPoint.x + pointerOffset;
        if (x + width > bounds.xMax)
            x = localPoint.x - pointerOffset - width;
        x = Mathf.Clamp(x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - width));

        float y = localPoint.y - pointerOffset;
        y = Mathf.Clamp(y, Mathf.Min(bounds.yMax, bounds.yMin + height), bounds.yMax);

        panelRect.anchoredPosition = new Vector2(x, y);
    }

    #endregion
}
