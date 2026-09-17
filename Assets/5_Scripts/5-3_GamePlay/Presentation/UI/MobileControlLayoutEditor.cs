using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 触屏玩法控件布局编辑器。预览直接实例化正式手机 HUD Prefab，只允许拖动带 MobileControlLayoutNode 的控件；
/// 保存时一次性写入归一化位置，取消不会改动玩家配置。
/// </summary>
[DisallowMultipleComponent]
public sealed class MobileControlLayoutEditor : MonoBehaviour
{
    #region 状态

    private static MobileControlLayoutEditor activeEditor;

    private readonly Dictionary<string, Vector2> defaultPositions =
        new Dictionary<string, Vector2>();

    private BasePanel ownerPanel;
    private BasePanel editorPanel;
    private RectTransform previewRoot;
    private GameObject previewObject;
    private MobileControlLayoutNode[] layoutNodes;
    private TextMeshProUGUI statusText;
    private Button saveButton;
    private Button resetButton;
    private Button cancelButton;
    private bool initialized;

    #endregion

    #region 打开与初始化

    /// <summary>从按键绑定页打开唯一布局编辑器实例。</summary>
    public static bool TryOpen(BasePanel owner, out string error)
    {
        error = null;
        if (activeEditor != null)
        {
            activeEditor.editorPanel?.Open();
            return true;
        }

        GameRes gameRes = GameRes.Instance;
        GameObject editorPrefab = gameRes?.GetPrefab(
            RuntimeUIPrefabKeys.MobileControlLayoutEditor,
            false);
        GameObject controlsPrefab = gameRes?.GetPrefab(RuntimeUIPrefabKeys.MobileControls, false);
        if (editorPrefab == null || controlsPrefab == null)
        {
            error = FlatWorldLocalizationService.GetUiText("触屏布局资源尚未准备好。");
            return false;
        }

        UIManager manager = UIManager.Instance;
        BasePanel panel = manager.CreatePanelFromGameObject(
            editorPrefab,
            RuntimeUIPrefabKeys.MobileControlLayoutEditor);
        manager.ConfigureGlobalOverlayPanel(panel);
        // 布局位置是相对 SafeAreaRoot 保存的；编辑预览也必须使用同一坐标系，
        // 否则刘海/打孔屏上会出现“编辑时一处、回到游戏另一处”的偏差。
        RectTransform safeAreaRoot = manager.SafeAreaRoot;
        RectTransform panelRect = panel.transform as RectTransform;
        if (safeAreaRoot != null && panelRect != null)
        {
            panelRect.SetParent(safeAreaRoot, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panelRect.SetAsLastSibling();
        }
        MobileControlLayoutEditor editor = panel.GetComponent<MobileControlLayoutEditor>();
        if (editor == null)
        {
            panel.Destroy();
            error = FlatWorldLocalizationService.GetUiText("触屏布局编辑器组件缺失。");
            return false;
        }

        editor.Initialize(owner, panel, controlsPrefab);
        activeEditor = editor;
        return true;
    }

    /// <summary>绑定正式编辑器 Prefab，并创建一份不会发送玩法输入的手机 HUD 预览。</summary>
    private void Initialize(BasePanel owner, BasePanel panel, GameObject controlsPrefab)
    {
        if (initialized)
            return;

        initialized = true;
        ownerPanel = owner;
        editorPanel = panel;
        previewRoot = FindComponent<RectTransform>(transform, "预览根");
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        saveButton = FindComponent<Button>(transform, "保存按钮");
        resetButton = FindComponent<Button>(transform, "恢复默认按钮");
        cancelButton = FindComponent<Button>(transform, "取消按钮");
        if (previewRoot == null || statusText == null || saveButton == null ||
            resetButton == null || cancelButton == null)
        {
            Debug.LogError("[MobileLayoutEditor] 正式编辑器 Prefab 节点契约不完整。", this);
            editorPanel.Destroy();
            return;
        }

        saveButton.onClick.AddListener(SaveAndClose);
        resetButton.onClick.AddListener(ResetPreviewToDefaults);
        cancelButton.onClick.AddListener(CancelAndClose);

        previewObject = Instantiate(controlsPrefab, previewRoot, false);
        previewObject.name = RuntimeUIPrefabKeys.MobileControls + "_LayoutPreview";
        FlatWorldUIAutoLocalizer.BindStaticTexts(previewObject.transform);
        PreparePreviewHierarchy();

        editorPanel.SetGameplayInputBlocking(true);
        editorPanel.PrepareForGamepadNavigation("保存按钮", true, true);
        editorPanel.CancelOverride = HandleCancel;
        editorPanel.CancelShortcutOverride = HandleCancelShortcut;
        editorPanel.Open();
        SetStatus("拖动按钮和摇杆到想要的位置；保存后会立即应用。", false);
    }

    /// <summary>关闭非玩法层、移除普通 UI 射线，仅给布局节点开启拖动。</summary>
    private void PreparePreviewHierarchy()
    {
        SetNamedObjectActive(previewObject.transform, "常驻控制层", false);
        SetNamedObjectActive(previewObject.transform, "快捷栏锚点", false);
        SetNamedObjectActive(previewObject.transform, "菜单抽屉", false);
        SetNamedObjectActive(previewObject.transform, "手机准线", false);
        SetNamedObjectActive(previewObject.transform, "普通指向区", false);
        SetNamedObjectActive(previewObject.transform, "手持物丢弃区", false);
        SetNamedObjectActive(previewObject.transform, "玩法控制层", true);

        Button[] previewButtons = previewObject.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < previewButtons.Length; index++)
            previewButtons[index].enabled = false;

        Graphic[] graphics = previewObject.GetComponentsInChildren<Graphic>(true);
        for (int index = 0; index < graphics.Length; index++)
            graphics[index].raycastTarget = false;

        layoutNodes = previewObject.GetComponentsInChildren<MobileControlLayoutNode>(true);
        defaultPositions.Clear();
        for (int index = 0; index < layoutNodes.Length; index++)
        {
            MobileControlLayoutNode node = layoutNodes[index];
            if (node == null || string.IsNullOrEmpty(node.ControlId))
                continue;

            node.PrepareForEditingPreview();
            defaultPositions[node.ControlId] = node.CaptureNormalizedPosition(previewRoot);
            node.ApplySavedPosition(previewRoot);
            node.SetEditing(previewRoot, true);
        }
    }

    #endregion

    #region 保存、恢复与取消

    /// <summary>保存当前预览中每个正式玩法控件的位置并关闭编辑器。</summary>
    private void SaveAndClose()
    {
        Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();
        if (layoutNodes != null)
        {
            for (int index = 0; index < layoutNodes.Length; index++)
            {
                MobileControlLayoutNode node = layoutNodes[index];
                if (node == null || string.IsNullOrEmpty(node.ControlId))
                    continue;

                positions[node.ControlId] = node.CaptureNormalizedPosition(previewRoot);
            }
        }

        UIUserSettings.SetMobileControlLayoutPositions(positions);
        CloseEditor();
    }

    /// <summary>只把当前预览恢复到正式 Prefab 默认位置；玩家确认保存后才真正覆盖配置。</summary>
    private void ResetPreviewToDefaults()
    {
        if (layoutNodes == null)
            return;

        for (int index = 0; index < layoutNodes.Length; index++)
        {
            MobileControlLayoutNode node = layoutNodes[index];
            if (node == null || !defaultPositions.TryGetValue(node.ControlId, out Vector2 position))
                continue;

            node.ApplyNormalizedPosition(previewRoot, position);
        }

        SetStatus("已恢复默认预览；点击保存后生效。", false);
    }

    private void CancelAndClose()
    {
        CloseEditor();
    }

    private bool HandleCancel(BaseEventData eventData)
    {
        eventData?.Use();
        CloseEditor();
        return true;
    }

    private bool HandleCancelShortcut()
    {
        CloseEditor();
        return true;
    }

    /// <summary>释放预览拖动状态并销毁临时面板，不触碰玩家尚未保存的草稿。</summary>
    private void CloseEditor()
    {
        if (layoutNodes != null)
        {
            for (int index = 0; index < layoutNodes.Length; index++)
                layoutNodes[index]?.SetEditing(previewRoot, false);
        }

        if (editorPanel != null)
        {
            editorPanel.CancelOverride = null;
            editorPanel.CancelShortcutOverride = null;
            editorPanel.Destroy();
        }

        ownerPanel?.RefreshGamepadNavigationState();
        if (activeEditor == this)
            activeEditor = null;
    }

    #endregion

    #region 辅助

    private void SetStatus(string sourceText, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = FlatWorldLocalizationService.GetUiText(sourceText);
        statusText.color = isError
            ? new Color(1f, 0.48f, 0.35f)
            : FlatWorldUITheme.TextSecondary;
    }

    private static void SetNamedObjectActive(Transform root, string objectName, bool active)
    {
        Transform target = FindTransform(root, objectName);
        if (target != null)
            target.gameObject.SetActive(active);
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        if (root == null)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == objectName)
                return transforms[index];
        }

        return null;
    }

    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        Transform target = FindTransform(root, objectName);
        return target != null ? target.GetComponent<T>() : null;
    }

    private void OnDestroy()
    {
        saveButton?.onClick.RemoveListener(SaveAndClose);
        resetButton?.onClick.RemoveListener(ResetPreviewToDefaults);
        cancelButton?.onClick.RemoveListener(CancelAndClose);
        if (activeEditor == this)
            activeEditor = null;
    }

    #endregion
}
