using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>复选下拉控件：视觉来自 Prefab，业务只提供稳定选项 ID 和选择变化回调。</summary>
[DisallowMultipleComponent]
public sealed class UIMultiSelectDropdown : MonoBehaviour
{
    #region 控件引用与选择状态

    public readonly struct Option
    {
        public readonly string Id;
        public readonly string Label;
        public Option(string id, string label) { Id = id; Label = label; }
    }

    [SerializeField] private Button trigger;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private TMP_Text heading;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button dismissArea;
    [SerializeField] private RectTransform popup;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private Toggle rowTemplate;

    private readonly List<Option> options = new();
    private readonly List<string> selected = new();
    private readonly List<Toggle> rows = new();
    private readonly Vector3[] corners = new Vector3[4];
    private Vector2 popupReferenceSize;
    private Vector3 popupReferenceScale;
    private UIManager subscribedUIManager;
    private bool applyingPopupLayout;
    private BasePanel owner;
    private Func<bool> previousShortcut;
    private Func<BaseEventData, bool> previousCancel;
    public IReadOnlyList<string> SelectedValues => selected;
    public bool IsExpanded => popup != null && popup.gameObject.activeSelf;
    public event Action SelectionChanged;

    #endregion

    #region 生命周期与业务绑定

    private void Awake()
    {
        popupReferenceSize = popup.rect.size;
        popupReferenceScale = popup.localScale;
    }

    private void OnEnable()
    {
        trigger.onClick.AddListener(ToggleExpanded);
        clearButton.onClick.AddListener(ClearSelection);
        dismissArea.onClick.AddListener(Close);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        UIUserSettings.Changed += RefreshExpandedPopupLayoutWithinSafeArea;
        subscribedUIManager = UIManager.ExistingInstance;
        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged += RefreshExpandedPopupLayoutWithinSafeArea;
        owner = GetComponentInParent<BasePanel>();
        if (owner != null)
        {
            owner.Closed += Close;
            previousShortcut = owner.CancelShortcutOverride;
            previousCancel = owner.CancelOverride;
            owner.CancelShortcutOverride = HandleCancelShortcut;
            owner.CancelOverride = HandleCancel;
        }
        RefreshLabels();
    }

    private void OnDisable()
    {
        Close();
        if (trigger != null) trigger.onClick.RemoveListener(ToggleExpanded);
        if (clearButton != null) clearButton.onClick.RemoveListener(ClearSelection);
        if (dismissArea != null) dismissArea.onClick.RemoveListener(Close);
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
        UIUserSettings.Changed -= RefreshExpandedPopupLayoutWithinSafeArea;
        if (!ReferenceEquals(subscribedUIManager, null))
            subscribedUIManager.InteractionSurfaceChanged -= RefreshExpandedPopupLayoutWithinSafeArea;
        subscribedUIManager = null;
        if (owner != null)
        {
            owner.Closed -= Close;
            if (owner.CancelShortcutOverride == HandleCancelShortcut) owner.CancelShortcutOverride = previousShortcut;
            if (owner.CancelOverride == HandleCancel) owner.CancelOverride = previousCancel;
        }
        owner = null;
    }

    private void OnRectTransformDimensionsChange() => RefreshExpandedPopupLayoutWithinSafeArea();

    public void SetOptions(IReadOnlyList<Option> values)
    {
        options.Clear();
        if (values != null)
            for (int i = 0; i < values.Count; i++) options.Add(values[i]);

        while (rows.Count < options.Count)
        {
            int index = rows.Count;
            Toggle row = Instantiate(rowTemplate, scroll.content, false);
            row.name = "Option_" + index;
            row.group = null; // 复选框不能加入单选 ToggleGroup。
            row.onValueChanged.AddListener(value => SetSelected(options[index].Id, value));
            rows.Add(row);
        }
        selected.RemoveAll(id => !options.Exists(option => option.Id == id));
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].gameObject.SetActive(i < options.Count);
            if (i < options.Count) rows[i].SetIsOnWithoutNotify(selected.Contains(options[i].Id));
        }
        RefreshLabels();
    }

    public void SetSelected(string id, bool value)
    {
        if (!options.Exists(option => option.Id == id) || selected.Contains(id) == value) return;
        if (value) selected.Add(id); else selected.Remove(id);
        for (int i = 0; i < options.Count; i++)
            rows[i].SetIsOnWithoutNotify(selected.Contains(options[i].Id));
        RefreshLabels();
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (selected.Count == 0) return;
        selected.Clear();
        foreach (Toggle row in rows) row.SetIsOnWithoutNotify(false);
        RefreshLabels();
        SelectionChanged?.Invoke();
    }

    private void OnLanguageChanged(string _) => RefreshLabels();

    private void RefreshLabels()
    {
        if (caption != null)
            caption.text = selected.Count == 0 ? FlatWorldLocalizationService.GetUiText("全部分类")
                : FlatWorldLocalizationService.GetUiFormat("分类（{0}）", selected.Count);
        if (heading != null) heading.text = FlatWorldLocalizationService.GetUiText("匹配任一所选分类");
        if (clearButton != null)
        {
            clearButton.interactable = selected.Count > 0;
            clearButton.GetComponentInChildren<TMP_Text>(true).text = FlatWorldLocalizationService.GetUiText("清除分类");
        }
        for (int i = 0; i < options.Count; i++)
            rows[i].GetComponentInChildren<TMP_Text>(true).text = FlatWorldLocalizationService.GetUiText(options[i].Label);
    }

    #endregion

    #region 展开、遮罩与返回

    private void ToggleExpanded()
    {
        if (IsExpanded) { Close(); return; }
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null || options.Count == 0) return;
        canvas = canvas.rootCanvas;
        RectTransform host = canvas.transform.Find("SafeAreaRoot") as RectTransform ?? (RectTransform)canvas.transform;

        // 弹层脱离窗口裁剪区，透明遮罩只在展开时拦截外部点击。
        RectTransform dismiss = (RectTransform)dismissArea.transform;
        dismiss.SetParent(host, false);
        dismiss.anchorMin = Vector2.zero;
        dismiss.anchorMax = Vector2.one;
        dismiss.offsetMin = dismiss.offsetMax = Vector2.zero;
        dismiss.gameObject.SetActive(true);
        dismiss.SetAsLastSibling();

        popup.SetParent(host, true);
        popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
        popup.pivot = new Vector2(1f, 1f);
        popup.sizeDelta = popupReferenceSize;
        popup.gameObject.SetActive(true);
        RefreshExpandedPopupLayoutWithinSafeArea();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1f;
        if (rows.Count > 0) rows[0].Select();
    }

    /// <summary>弹层继承控件实际比例，并在安全区放不下时整体限幅，列表继续使用自身滚动。</summary>
    public void RefreshExpandedPopupLayoutWithinSafeArea()
    {
        if (!IsExpanded || applyingPopupLayout || popup.parent is not RectTransform host) return;
        applyingPopupLayout = true;
        try
        {
            float scaleX = host.InverseTransformVector(transform.TransformVector(
                Vector3.right * popupReferenceScale.x)).magnitude;
            float scaleY = host.InverseTransformVector(transform.TransformVector(
                Vector3.up * popupReferenceScale.y)).magnitude;
            Vector2 available = host.rect.size - Vector2.one * 16f;
            float fit = Mathf.Min(1f, Mathf.Max(0f, available.x) / (popupReferenceSize.x * scaleX),
                Mathf.Max(0f, available.y) / (popupReferenceSize.y * scaleY));
            popup.localScale = new Vector3(scaleX * fit, scaleY * fit, popupReferenceScale.z);

            ((RectTransform)trigger.transform).GetWorldCorners(corners);
            Vector3 target = host.InverseTransformPoint(corners[3]);
            float width = popupReferenceSize.x * popup.localScale.x;
            float height = popupReferenceSize.y * popup.localScale.y;
            target.x = Mathf.Clamp(target.x, host.rect.xMin + width + 8f, host.rect.xMax - 8f);
            target.y = Mathf.Clamp(target.y - 8f, host.rect.yMin + height + 8f, host.rect.yMax - 8f);
            popup.localPosition = new Vector3(target.x, target.y, 0f);
            dismissArea.transform.SetAsLastSibling();
            popup.SetAsLastSibling();
        }
        finally { applyingPopupLayout = false; }
    }

    public void Close()
    {
        if (popup == null || dismissArea == null) return;
        bool wasOpen = IsExpanded;
        popup.gameObject.SetActive(false);
        dismissArea.gameObject.SetActive(false);
        popup.SetParent(transform, false);
        popup.localScale = popupReferenceScale;
        dismissArea.transform.SetParent(transform, false);
        if (wasOpen && isActiveAndEnabled && trigger != null) trigger.Select();
    }

    private bool HandleCancelShortcut()
    {
        if (!IsExpanded) return previousShortcut?.Invoke() ?? false;
        Close();
        return true;
    }

    private bool HandleCancel(BaseEventData data)
    {
        if (!IsExpanded) return previousCancel?.Invoke(data) ?? false;
        Close();
        data?.Use();
        return true;
    }

    #endregion
}
