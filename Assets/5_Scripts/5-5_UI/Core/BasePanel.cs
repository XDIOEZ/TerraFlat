using Sirenix.OdinInspector;
// AI-Context: 双脚本 UI 的面板视图基类；按节点名收集控件并管理开关状态，视觉结构完全由 Prefab 决定。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通用面板视图：按节点名缓存控件、管理开关状态并补齐本地化、音频和手柄导航能力。
/// 层级结构只在初始化或显式标脏后扫描一次；动态列表完成增删后调用 RefreshUIComponents 即可重建快照。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class BasePanel : MonoBehaviour, ICancelHandler
{
    // 每种UI类型都有一个字典 用来存储UI组件 名字就用挂接的gameObject.name 作为Key
    private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    private readonly Dictionary<string, TMP_InputField> inputFields = new Dictionary<string, TMP_InputField>();
    private readonly Dictionary<string, TextMeshProUGUI> textElements = new Dictionary<string, TextMeshProUGUI>();
    private readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
    private readonly Dictionary<string, ScrollRect> scrollRects = new Dictionary<string, ScrollRect>();
    private readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
    private readonly List<Component> hierarchyComponents = new List<Component>(128);
    private readonly List<Button> cachedButtons = new List<Button>(32);
    private readonly List<TMP_Text> cachedTexts = new List<TMP_Text>(32);
    private readonly List<Selectable> cachedSelectables = new List<Selectable>(32);
    private readonly HashSet<Button> autoBoundButtons = new HashSet<Button>();
    private readonly HashSet<Toggle> autoBoundToggles = new HashSet<Toggle>();
    private readonly HashSet<Button> autoBoundCloseButtons = new HashSet<Button>();
    private readonly HashSet<Button> autoBoundDestroyButtons = new HashSet<Button>();

    private bool hierarchySnapshotDirty = true;
    private int hierarchySnapshotVersion;
    private int navigationSnapshotVersion = -1;

    /// <summary>Profiler 可读取的层级快照重建次数。</summary>
    public int HierarchySnapshotRebuildCount { get; private set; }

    /// <summary>当前快照内缓存的可选控件数量。</summary>
    public int CachedSelectableCount => cachedSelectables.Count;

    /// <summary>
    /// 当前全局层级顺序，确保拖拽物体始终显示在最上层
    /// </summary>
    [ShowInInspector]
    [ReadOnly]
    public static int CurrentOrder = 0;

    /// <summary>
    /// 提升UI元素到最上层显示
    /// </summary>
    public static void BringToFront(RectTransform rectTransform, Canvas canvas = null)
    {
        if (rectTransform == null)
            return;

        // 增加全局层级计数器
        BasePanel.CurrentOrder++;
        // 设置当前元素的兄弟索引
        rectTransform.SetSiblingIndex(BasePanel.CurrentOrder);

        if (canvas != null)
        {
            canvas.sortingOrder = BasePanel.CurrentOrder;
        }

        NotifyInteractionSurfaceChanged();
    }
    public static void BringToBack(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

        // 设置当前元素的兄弟索引
        rectTransform.SetSiblingIndex(BasePanel.CurrentOrder - 1);
        NotifyInteractionSurfaceChanged();
    }

    public CanvasGroup canvasGroup;
    public bool CanDrag = false;
    public UI_Drag Dragger;
    public RectTransform rectTransform;
    public string PanelName;

    // 记录面板的开关状态
    [SerializeField]
    private bool isOpen = false;
    private bool gamepadNavigationPrepared;
    private bool closeOnGamepadCancel;
    private bool closeOnEscapeShortcut;
    private string preferredSelectableName;
    private GameObject previousSelectedObject;

    public event Action Opened;
    public event Action Closed;
    /// <summary>领域面板可优先消费取消操作，例如关闭危险确认层而不是直接关闭整个面板。</summary>
    public Func<BaseEventData, bool> CancelOverride { get; set; }

    /// <summary>
    /// 是否属于可由全局取消快捷键关闭的临时面板。
    /// 只有显式启用导航/取消契约的面板会参与，常驻 HUD 不受影响。
    /// </summary>
    public bool IsCancelShortcutTarget =>
        gamepadNavigationPrepared && closeOnEscapeShortcut && isOpen && gameObject.activeInHierarchy;

    /// <summary>
    /// 当前面板是否已经接入手柄导航契约。
    /// </summary>
    public bool IsGamepadNavigationPrepared => gamepadNavigationPrepared;

    private void Awake()
    {
        EnsureRuntimeReferences();
        EnsureHierarchySnapshot();
        FlatWorldUITheme.ApplyGamepadNavigationPolicy(cachedSelectables);
    }

    #region  Unity生命周期
    public void Init()
    {
        EnsureHierarchySnapshot();
        EnsureRuntimeReferences();
        ApplyCachedRuntimeBindings();

        // 初始化面板状态
        if (canvasGroup != null)
        {
            isOpen = canvasGroup.alpha > 0 && canvasGroup.interactable && canvasGroup.blocksRaycasts;
        }

    }

    public void SetPanelName(string name)
    {
        PanelName = name;
        GetText("信息").text = PanelName;
        UIManager.Instance.RegisterPanel(this, PanelName);
    }

    void OnValidate()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        //TODO 自动设置CanvasScaler的UI Scale Mode为Scale With Screen Size
        CanvasScaler canvasScaler = GetComponent<CanvasScaler>();
        if (canvasScaler != null)
        {
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            //TODO 设置为1920 * 1080
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
        }
        Dragger = GetComponent<UI_Drag>();
    }

    private void EnsureRuntimeReferences()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        CanDrag = Dragger != null;
    }
    #endregion

    #region 层级快照

    /// <summary>
    /// 自动收集所有子对象上的UI组件
    /// </summary>
    public void CollectUIComponents()
    {
        MarkHierarchyDirty();
        EnsureHierarchySnapshot();
    }

    /// <summary>标记面板结构已变化；下次查询前只重建一次快照。</summary>
    public void MarkHierarchyDirty()
    {
        bool wasDirty = hierarchySnapshotDirty;
        hierarchySnapshotDirty = true;
        navigationSnapshotVersion = -1;
        if (!wasDirty)
            NotifyInteractionSurfaceChanged();
    }

    /// <summary>直属子节点变化时失效快照；深层动态列表仍由 RefreshUIComponents 显式提交。</summary>
    private void OnTransformChildrenChanged()
    {
        MarkHierarchyDirty();
    }

    /// <summary>使用单次 Component 层级遍历重建全部名称字典和运行时列表。</summary>
    private void EnsureHierarchySnapshot()
    {
        if (!hierarchySnapshotDirty)
            return;

        EnsureRuntimeReferences();

        autoBoundButtons.RemoveWhere(button => button == null);
        autoBoundToggles.RemoveWhere(toggle => toggle == null);
        autoBoundCloseButtons.RemoveWhere(button => button == null);
        autoBoundDestroyButtons.RemoveWhere(button => button == null);

        buttons.Clear();
        inputFields.Clear();
        textElements.Clear();
        toggles.Clear();
        sliders.Clear();
        scrollRects.Clear();
        images.Clear();
        hierarchyComponents.Clear();
        cachedButtons.Clear();
        cachedTexts.Clear();
        cachedSelectables.Clear();
        Dragger = null;
        CanDrag = false;

        GetComponentsInChildren<Component>(true, hierarchyComponents);
        for (int i = 0; i < hierarchyComponents.Count; i++)
            CacheComponent(hierarchyComponents[i]);

        BindLifecycleButtons();
        CanDrag = Dragger != null;
        hierarchySnapshotDirty = false;
        hierarchySnapshotVersion++;
        navigationSnapshotVersion = -1;
        HierarchySnapshotRebuildCount++;
    }

    /// <summary>把单个组件归类到名称字典与复用列表。</summary>
    private void CacheComponent(Component component)
    {
        if (component == null)
            return;

        if (component is Button button)
        {
            cachedButtons.Add(button);
            if (!buttons.ContainsKey(button.name))
            {
                buttons[button.name] = button;
                if (autoBoundButtons.Add(button))
                    button.onClick.AddListener(() => OnClick(button.name));
            }
        }

        if (component is TMP_InputField inputField && !inputFields.ContainsKey(inputField.name))
            inputFields[inputField.name] = inputField;

        if (component is TextMeshProUGUI text && !textElements.ContainsKey(text.name))
            textElements[text.name] = text;

        if (component is TMP_Text tmpText)
            cachedTexts.Add(tmpText);

        if (component is Toggle toggle && !toggles.ContainsKey(toggle.name))
        {
            toggles[toggle.name] = toggle;
            if (autoBoundToggles.Add(toggle))
                toggle.onValueChanged.AddListener(value => OnValueChanged(toggle.name, value));
        }

        if (component is Slider slider && !sliders.ContainsKey(slider.name))
            sliders[slider.name] = slider;

        if (component is ScrollRect scrollRect && !scrollRects.ContainsKey(scrollRect.name))
            scrollRects[scrollRect.name] = scrollRect;

        if (component is Image image && !images.ContainsKey(image.name))
            images[image.name] = image;

        if (component is Selectable selectable)
            cachedSelectables.Add(selectable);

        if (Dragger == null && component is UI_Drag dragger)
            Dragger = dragger;
    }

    /// <summary>生命周期按钮只绑定一次，防止动态刷新累积监听。</summary>
    private void BindLifecycleButtons()
    {
        if (buttons.TryGetValue("关闭", out Button closeButton) && autoBoundCloseButtons.Add(closeButton))
            closeButton.onClick.AddListener(Close);

        if (buttons.TryGetValue("销毁", out Button destroyButton) && autoBoundDestroyButtons.Add(destroyButton))
            destroyButton.onClick.AddListener(DestroyPanelObject);
    }

    /// <summary>复用当前快照补齐本地化、音频、颜色和微交互组件。</summary>
    private void ApplyCachedRuntimeBindings()
    {
        EnsureHierarchySnapshot();
        FlatWorldUIAutoLocalizer.BindStaticTexts(cachedTexts);
        FlatWorldAudioUIFeedback.EnsureFor(cachedButtons);
        FlatWorldUITheme.ApplySelectionColors(cachedSelectables);
        FlatWorldUIFeedback.EnsureFor(cachedButtons);
    }

    private void DestroyPanelObject()
    {
        Destroy(gameObject);
    }

    #endregion

    #region 面板显示控制

    public void PrepareForGamepadNavigation(
        string preferredControlName = null,
        bool closeOnCancel = true,
        bool closeOnEscape = true)
    {
        bool contractChanged = !gamepadNavigationPrepared ||
                               closeOnGamepadCancel != closeOnCancel ||
                               closeOnEscapeShortcut != closeOnEscape ||
                               !string.Equals(
                                   preferredSelectableName,
                                   preferredControlName,
                                   StringComparison.Ordinal);
        gamepadNavigationPrepared = true;
        closeOnGamepadCancel = closeOnCancel;
        closeOnEscapeShortcut = closeOnEscape;
        preferredSelectableName = preferredControlName;
        EnsureGamepadNavigationSnapshot();

        if (contractChanged)
            NotifyInteractionSurfaceChanged();

        if (isOpen)
            SelectDefaultForGamepad();
    }

    /// <summary>
    /// 设置该面板是否参与全局 Escape 关闭路由；常驻 HUD 应显式传入 false。
    /// </summary>
    public void SetEscapeShortcutEnabled(bool enabled)
    {
        if (closeOnEscapeShortcut == enabled)
            return;

        closeOnEscapeShortcut = enabled;
        NotifyInteractionSurfaceChanged();
    }

    [Button]
    public void Open()
    {
        bool wasOpen = isOpen;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            isOpen = true;
        }
        // 提升层级以显示在最上层
        BasePanel.BringToFront(rectTransform);

        if (gamepadNavigationPrepared)
        {
            EnsureGamepadNavigationSnapshot();
            SelectDefaultForGamepad();
        }

        NotifyInteractionSurfaceChanged();
        if (!wasOpen && isOpen)
            Opened?.Invoke();
    }

    [Button]
    public void Close()
    {
        bool wasOpen = isOpen;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            isOpen = false;
            // 层级以显示在最上层
            BasePanel.BringToBack(rectTransform);
        }

        NotifyInteractionSurfaceChanged();
        if (wasOpen && !isOpen)
        {
            RestorePreviousSelection();
            Closed?.Invoke();
        }
    }

    public bool IsOpen()
    {
        // 使用记录的状态而不是每次都检查CanvasGroup属性
        return isOpen;
    }

    public bool IsVisible()
    {
        // 使用记录的状态而不是每次都检查CanvasGroup属性
        return isOpen;
    }
    /// <summary>
    /// 切换当前面板的显示状态
    /// 如果当前是打开状态则关闭，否则打开
    /// </summary>
    public void Toggle()
    {
        if (IsOpen())
            Close();
        else
            Open();
    }

    public void Destroy()
    {
        Close();
        UIManager.Instance.DestroyPanel(this);
    }

    #endregion

    public void OnCancel(BaseEventData eventData)
    {
        if (!gamepadNavigationPrepared || !closeOnGamepadCancel || !isOpen)
            return;

        if (CancelOverride != null && CancelOverride(eventData))
        {
            UIManager.Instance.NotifyCancelHandled();
            return;
        }

        eventData.Use();
        UIManager.Instance.NotifyCancelHandled();
        Close();
    }

    private void EnsureAutomaticNavigation()
    {
        for (int i = 0; i < cachedSelectables.Count; i++)
        {
            Selectable selectable = cachedSelectables[i];
            if (selectable == null || FlatWorldUITheme.IsGamepadNavigationExcluded(selectable))
                continue;

            Navigation navigation = selectable.navigation;
            bool emptyExplicitNavigation = navigation.mode == Navigation.Mode.Explicit &&
                navigation.selectOnUp == null &&
                navigation.selectOnDown == null &&
                navigation.selectOnLeft == null &&
                navigation.selectOnRight == null;
            if (navigation.mode != Navigation.Mode.None && !emptyExplicitNavigation)
                continue;

            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }
    }

    /// <summary>
    /// 选择当前面板的默认手柄控件，供动态面板在完成子节点创建后重新调用。
    /// </summary>
    public void SelectDefaultForGamepad()
    {
        if (!EventSystemGuard.IsGamepadMode)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        GameObject selectedObject = eventSystem.currentSelectedGameObject;
        if (selectedObject == null || !selectedObject.transform.IsChildOf(transform))
            previousSelectedObject = selectedObject;

        Selectable selectable = FindPreferredSelectable();
        if (selectable == null)
            return;

        eventSystem.SetSelectedGameObject(null);
        eventSystem.SetSelectedGameObject(selectable.gameObject);
    }

    private Selectable FindPreferredSelectable()
    {
        EnsureHierarchySnapshot();
        if (!string.IsNullOrEmpty(preferredSelectableName))
        {
            for (int i = 0; i < cachedSelectables.Count; i++)
            {
                Selectable selectable = cachedSelectables[i];
                if (CanSelect(selectable) &&
                    (selectable.name == preferredSelectableName ||
                     selectable.name.StartsWith(preferredSelectableName, StringComparison.OrdinalIgnoreCase)))
                    return selectable;
            }
        }

        Selectable fallback = null;
        for (int i = 0; i < cachedSelectables.Count; i++)
        {
            Selectable selectable = cachedSelectables[i];
            if (!CanSelect(selectable))
                continue;

            if (selectable is Button || selectable is Toggle || selectable is Slider)
                return selectable;

            fallback ??= selectable;
        }

        return fallback;
    }

    private static bool CanSelect(Selectable selectable)
    {
        return selectable != null &&
               !FlatWorldUITheme.IsGamepadNavigationExcluded(selectable) &&
               selectable.gameObject.activeInHierarchy &&
               selectable.IsInteractable() &&
               selectable.navigation.mode != Navigation.Mode.None;
    }

    private void RestorePreviousSelection()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        GameObject selectedObject = eventSystem.currentSelectedGameObject;
        if (selectedObject != null && !selectedObject.transform.IsChildOf(transform))
            return;

        GameObject restoreTarget = previousSelectedObject;
        previousSelectedObject = null;
        eventSystem.SetSelectedGameObject(
            restoreTarget != null && restoreTarget.activeInHierarchy ? restoreTarget : null);
    }

    /// <summary>
    /// 为滚动容器内的可选控件补充自动滚动跟随器。
    /// </summary>
    private void EnsureSelectionFollowers()
    {
        for (int i = 0; i < cachedSelectables.Count; i++)
        {
            Selectable selectable = cachedSelectables[i];
            if (selectable == null || FlatWorldUITheme.IsGamepadNavigationExcluded(selectable))
                continue;

            if (selectable.GetComponentInParent<ScrollRect>() != null &&
                selectable.GetComponent<GamepadUISelectionFollower>() == null)
            {
                selectable.gameObject.AddComponent<GamepadUISelectionFollower>();
            }
        }
    }

    /// <summary>为输入框补充确认前的方向导航桥接，不改变其 Selectable 焦点表现。</summary>
    private void EnsureInputFieldNavigationBridges()
    {
        foreach (TMP_InputField inputField in inputFields.Values)
        {
            if (inputField != null && inputField.GetComponent<GamepadInputFieldNavigationBridge>() == null)
                inputField.gameObject.AddComponent<GamepadInputFieldNavigationBridge>();
        }
    }

    /// <summary>每个层级版本只补齐一次导航与滚动跟随组件。</summary>
    private void EnsureGamepadNavigationSnapshot()
    {
        EnsureHierarchySnapshot();
        if (navigationSnapshotVersion == hierarchySnapshotVersion)
            return;

        FlatWorldUITheme.ApplyGamepadNavigationPolicy(cachedSelectables);
        EnsureAutomaticNavigation();
        EnsureSelectionFollowers();
        EnsureInputFieldNavigationBridges();
        navigationSnapshotVersion = hierarchySnapshotVersion;
    }

    private void OnDestroy()
    {
        NotifyInteractionSurfaceChanged();
        if (isOpen)
        {
            isOpen = false;
            Closed?.Invoke();
        }

        Opened = null;
        Closed = null;
        CancelOverride = null;
    }

    /// <summary>对象启停会改变活动面板集合，必须让顶层查询缓存失效。</summary>
    private void OnEnable()
    {
        NotifyInteractionSurfaceChanged();
    }

    private void OnDisable()
    {
        NotifyInteractionSurfaceChanged();
    }

    #region 按钮操作

    /// <summary>
    /// 获取按钮组件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <returns>按钮组件，如果不存在返回null</returns>
    public Button GetButton(string buttonName)
    {
        EnsureHierarchySnapshot();
        if (buttons.TryGetValue(buttonName, out Button button))
        {
            return button;
        }
        Debug.LogWarning($"未找到名为 {buttonName} 的按钮,按钮的数量为:{buttons.Count}");
        return null;
    }

    /// <summary>
    /// 设置按钮点击事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="onClick">点击回调</param>
    public void SetButtonOnClick(string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.onClick.AddListener(onClick);
        }
    }

    /// <summary>
    /// 设置按钮按下事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="onPress">按下回调</param>
    public void SetButtonOnPress(string buttonName, UnityEngine.Events.UnityAction onPress)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = button.gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerDown;
            entry.callback.AddListener((data) => { onPress?.Invoke(); });
            trigger.triggers.Add(entry);
        }
    }

    /// <summary>
    /// 设置按钮松开事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="onRelease">松开回调</param>
    public void SetButtonOnRelease(string buttonName, UnityEngine.Events.UnityAction onRelease)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = button.gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerUp;
            entry.callback.AddListener((data) => { onRelease?.Invoke(); });
            trigger.triggers.Add(entry);
        }
    }

    /// <summary>
    /// 显示/隐藏按钮
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetButtonVisible(string buttonName, bool isVisible)
    {
        Button button = GetButton(buttonName);
        if (button != null)
            SetObjectActive(button.gameObject, isVisible);
    }

    #endregion

    #region 输入框操作

    /// <summary>
    /// 获取输入框组件
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <returns>输入框组件，如果不存在返回null</returns>
    public TMP_InputField GetInputField(string inputFieldName)
    {
        EnsureHierarchySnapshot();
        if (inputFields.TryGetValue(inputFieldName, out TMP_InputField inputField))
        {
            return inputField;
        }
        Debug.LogWarning($"未找到名为 {inputFieldName} 的输入框");
        return null;
    }

    /// <summary>
    /// 设置输入框文本
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="text">文本内容</param>
    public void SetInputFieldText(string inputFieldName, string text)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.text = text;
        }
    }

    /// <summary>
    /// 获取输入框文本
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <returns>输入框文本内容</returns>
    public string GetInputFieldText(string inputFieldName)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            return inputField.text;
        }
        return "";
    }

    /// <summary>
    /// 设置输入框是否可交互
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="isInteractable">是否可交互</param>
    public void SetInputFieldInteractable(string inputFieldName, bool isInteractable)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.interactable = isInteractable;
        }
    }

    /// <summary>
    /// 显示/隐藏输入框
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetInputFieldVisible(string inputFieldName, bool isVisible)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
            SetObjectActive(inputField.gameObject, isVisible);
    }

    #endregion

    #region 文本操作

    /// <summary>
    /// 获取文本组件
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <returns>文本组件，如果不存在返回null</returns>
    public TextMeshProUGUI GetText(string textName)
    {
        if (TryGetText(textName, out TextMeshProUGUI text))
            return text;

        Debug.LogWarning($"未找到名为 {textName} 的文本组件");

        Transform parent = transform;
        Debug.Log($"子对象数量: {transform.childCount}");

        for (int i = 0; i < parent.childCount; i++)
        {
            Debug.Log($"子对象 {i}: {parent.GetChild(i).name}");
        }

        return null;
    }

    /// <summary>安静查询可选文本组件。</summary>
    public bool TryGetText(string textName, out TextMeshProUGUI text)
    {
        EnsureHierarchySnapshot();
        return textElements.TryGetValue(textName, out text);
    }

    /// <summary>
    /// 设置文本内容
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="text">文本内容</param>
    public void SetText(string textName, string text)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.text = text;
        }
    }

    /// <summary>
    /// 获取文本内容
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <returns>文本内容</returns>
    public string GetTextContent(string textName)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            return textElement.text;
        }
        return "";
    }

    /// <summary>
    /// 设置文本颜色
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="color">颜色</param>
    public void SetTextColor(string textName, Color color)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.color = color;
        }
    }

    /// <summary>
    /// 显示/隐藏文本
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetTextVisible(string textName, bool isVisible)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
            SetObjectActive(textElement.gameObject, isVisible);
    }

    #endregion

    #region Toggle操作

    /// <summary>
    /// 获取Toggle组件
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <returns>Toggle组件，如果不存在返回null</returns>
    public Toggle GetToggle(string toggleName)
    {
        EnsureHierarchySnapshot();
        if (toggles.TryGetValue(toggleName, out Toggle toggle))
        {
            return toggle;
        }
        Debug.LogWarning($"未找到名为 {toggleName} 的Toggle");
        return null;
    }

    /// <summary>
    /// 设置Toggle是否选中
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <param name="isOn">是否选中</param>
    public void SetToggleIsOn(string toggleName, bool isOn)
    {
        Toggle toggle = GetToggle(toggleName);
        if (toggle != null)
        {
            toggle.isOn = isOn;
        }
    }

    /// <summary>
    /// 获取Toggle是否选中
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <returns>Toggle是否选中</returns>
    public bool GetToggleIsOn(string toggleName)
    {
        Toggle toggle = GetToggle(toggleName);
        if (toggle != null)
        {
            return toggle.isOn;
        }
        return false;
    }

    #endregion

    #region Slider操作

    /// <summary>
    /// 获取Slider组件
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <returns>Slider组件，如果不存在返回null</returns>
    public Slider GetSlider(string sliderName)
    {
        EnsureHierarchySnapshot();
        if (sliders.TryGetValue(sliderName, out Slider slider))
        {
            return slider;
        }
        Debug.LogWarning($"未找到名为 {sliderName} 的Slider");
        return null;
    }

    /// <summary>
    /// 设置Slider值
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <param name="value">值</param>
    public void SetSliderValue(string sliderName, float value)
    {
        Slider slider = GetSlider(sliderName);
        if (slider != null)
        {
            slider.value = value;
        }
    }

    /// <summary>
    /// 获取Slider值
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <returns>Slider值</returns>
    public float GetSliderValue(string sliderName)
    {
        Slider slider = GetSlider(sliderName);
        if (slider != null)
        {
            return slider.value;
        }
        return 0f;
    }

    #endregion

    #region Image操作

    /// <summary>
    /// 获取Image组件
    /// </summary>
    /// <param name="imageName">Image名称</param>
    /// <returns>Image组件，如果不存在返回null</returns>
    public Image GetImage(string imageName)
    {
        EnsureHierarchySnapshot();
        if (images.TryGetValue(imageName, out Image image))
        {
            return image;
        }
        Debug.LogWarning($"未找到名为 {imageName} 的Image");
        return null;
    }

    #endregion

    #region 通用操作

    /// <summary>
    /// 显示/隐藏任意UI组件
    /// </summary>
    /// <param name="uiName">UI组件名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetUIVisible(string uiName, bool isVisible)
    {
        EnsureHierarchySnapshot();
        // 检查是否为按钮
        if (buttons.ContainsKey(uiName))
        {
            SetButtonVisible(uiName, isVisible);
            return;
        }

        // 检查是否为输入框
        if (inputFields.ContainsKey(uiName))
        {
            SetInputFieldVisible(uiName, isVisible);
            return;
        }

        // 检查是否为文本
        if (textElements.ContainsKey(uiName))
        {
            SetTextVisible(uiName, isVisible);
            return;
        }

        // 检查是否为Toggle
        if (toggles.ContainsKey(uiName))
        {
            SetObjectActive(toggles[uiName].gameObject, isVisible);
            return;
        }

        // 检查是否为Slider
        if (sliders.ContainsKey(uiName))
        {
            SetObjectActive(sliders[uiName].gameObject, isVisible);
            return;
        }

        // 检查是否为Image
        if (images.ContainsKey(uiName))
        {
            SetObjectActive(images[uiName].gameObject, isVisible);
            return;
        }

        Debug.LogWarning($"未找到名为 {uiName} 的UI组件");
    }

    /// <summary>
    /// 重新收集所有UI组件（当动态添加UI组件时调用）
    /// </summary>
    public void RefreshUIComponents()
    {
        MarkHierarchyDirty();
        EnsureHierarchySnapshot();
        ApplyCachedRuntimeBindings();
        if (gamepadNavigationPrepared)
            EnsureGamepadNavigationSnapshot();

        NotifyInteractionSurfaceChanged();
    }

    /// <summary>
    /// 页面显隐或交互状态变化时仅刷新导航契约，不重新扫描没有变化的层级。
    /// </summary>
    public void RefreshGamepadNavigationState()
    {
        EnsureHierarchySnapshot();
        navigationSnapshotVersion = -1;
        if (gamepadNavigationPrepared)
            EnsureGamepadNavigationSnapshot();

        NotifyInteractionSurfaceChanged();
    }

    /// <summary>仅在显隐实际变化时通知虚拟光标重新命中。</summary>
    private void SetObjectActive(GameObject target, bool active)
    {
        if (target == null || target.activeSelf == active)
            return;

        target.SetActive(active);
        NotifyInteractionSurfaceChanged();
    }

    /// <summary>通知全局 UI 交互面发生变化。</summary>
    private static void NotifyInteractionSurfaceChanged()
    {
        UIManager.ExistingInstance?.NotifyInteractionSurfaceChanged();
    }

    #endregion

    #region 事件处理

    /// <summary>
    /// 按钮点击事件响应
    /// 通过子类重写来处理不同按钮的点击逻辑
    /// </summary>
    /// <param name="btnName">按钮名称</param>
    private void OnClick(string btnName)
    {

    }

    /// <summary>
    /// Toggle开关值改变事件响应
    /// 通过子类重写来处理不同Toggle的值变化逻辑
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <param name="value">Toggle的当前值</param>
    private void OnValueChanged(string toggleName, bool value)
    {

    }

    #endregion
}
