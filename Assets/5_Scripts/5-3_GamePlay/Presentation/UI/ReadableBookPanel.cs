using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>双页书籍阅读面板；书页、专用物品槽和扩页按钮由 UI_ReadableBook Prefab 提供。</summary>
public sealed class ReadableBookPanel : MonoBehaviour
{
    #region Prefab 控件

    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI leftPageText;
    [SerializeField] private TextMeshProUGUI rightPageText;
    [SerializeField] private TextMeshProUGUI leftPageNumber;
    [SerializeField] private TextMeshProUGUI rightPageNumber;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    private TextMeshProUGUI leftPageTitleText; // 左页标题文本。
    private TextMeshProUGUI rightPageTitleText; // 右页标题文本。
    private TextMeshProUGUI writingMaterialLabel; // 书写材料槽提示。
    private TextMeshProUGUI paperLabel; // 扩页纸张槽提示。
    private TextMeshProUGUI addPaperPageLabel; // 扩页按钮文案。
    private ItemSlot_UI writingMaterialSlot; // 解锁书写的输入槽。
    private ItemSlot_UI paperSlot; // 消耗纸张的输入槽。
    private Button addPaperPageButton; // 追加空白页按钮。

    #endregion

    #region 运行态

    private BasePanel panel;
    private Mod_ReadableBook book;
    private Item actor;
    private Inventory bookInventory;
    private ReadableBookTextEditor leftTitleEditor;
    private ReadableBookTextEditor leftBodyEditor;
    private ReadableBookTextEditor rightTitleEditor;
    private ReadableBookTextEditor rightBodyEditor;
    private int spreadStart;
    private static ReadableBookPanel current;

    #endregion

    #region 生命周期与打开关闭

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_ReadableBook.OpenRequested -= Show;
        Mod_ReadableBook.OpenRequested += Show;
    }

    /// <summary>复用唯一阅读面板；切换到另一份读物时从第一页重新打开。</summary>
    private static void Show(Mod_ReadableBook target, Item owner)
    {
        if (target == null || !target.CanRead(owner))
            return;

        if (current == null)
        {
            GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.ReadableBook, false);
            if (prefab == null)
                throw new InvalidOperationException($"缺少可阅读书籍 UI Prefab：{RuntimeUIPrefabKeys.ReadableBook}");

            current = UIManager.Instance.CreatePanelFromGameObject(prefab).GetComponent<ReadableBookPanel>();
            if (current == null)
                throw new InvalidOperationException("UI_ReadableBook Prefab 缺少 ReadableBookPanel 组件。");
        }

        current.FinishTextEditing();
        current.ClearTarget();
        current.book = target;
        current.actor = owner;
        current.spreadStart = 0;
        current.BindInputInventory();
        current.Refresh();
        current.panel.Open();
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        if (panel == null)
            throw new MissingComponentException("UI_ReadableBook 缺少 BasePanel。");

        BindPrefabControls();
        leftTitleEditor = EnsureTextEditor(leftPageTitleText, false);
        leftBodyEditor = EnsureTextEditor(leftPageText, true);
        rightTitleEditor = EnsureTextEditor(rightPageTitleText, false);
        rightBodyEditor = EnsureTextEditor(rightPageText, true);

        previousButton.onClick.AddListener(PreviousSpread);
        nextButton.onClick.AddListener(NextSpread);
        addPaperPageButton.onClick.AddListener(AddPaperPage);
        panel.Closed += OnPanelClosed;
        panel.PrepareForGamepadNavigation(nextButton.name, true, true);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>玩家切走物品、丢下读物或目标销毁时立即关闭。</summary>
    private void Update()
    {
        if (panel.IsOpen() && (book == null || !book.CanRead(actor)))
            panel.Close();
    }

    private void BindPrefabControls()
    {
        Transform frame = transform.Find("书籍框");
        leftPageTitleText = FindText(frame, "左页/左页标题");
        rightPageTitleText = FindText(frame, "右页/右页标题");
        writingMaterialSlot = FindSlot(frame, "书写材料槽");
        paperSlot = FindSlot(frame, "纸张槽");
        addPaperPageButton = frame != null
            ? frame.Find("添加纸张")?.GetComponent<Button>()
            : null;
        writingMaterialLabel = FindText(frame, "书写材料标签");
        paperLabel = FindText(frame, "纸张标签");
        addPaperPageLabel = FindText(frame, "添加纸张/文字");

        if (titleText == null || leftPageTitleText == null || rightPageTitleText == null ||
            leftPageText == null || rightPageText == null || leftPageNumber == null || rightPageNumber == null ||
            previousButton == null || nextButton == null || addPaperPageButton == null ||
            writingMaterialSlot == null || paperSlot == null || writingMaterialLabel == null ||
            paperLabel == null || addPaperPageLabel == null)
        {
            throw new MissingReferenceException("UI_ReadableBook 缺少书页标题、物品槽或扩页控件。");
        }
    }

    private static TextMeshProUGUI FindText(Transform root, string path)
    {
        return root != null ? root.Find(path)?.GetComponent<TextMeshProUGUI>() : null;
    }

    private static ItemSlot_UI FindSlot(Transform root, string name)
    {
        return root != null ? root.Find(name)?.GetComponent<ItemSlot_UI>() : null;
    }

    private ReadableBookTextEditor EnsureTextEditor(TextMeshProUGUI text, bool multiline)
    {
        ReadableBookTextEditor editor = text.GetComponent<ReadableBookTextEditor>();
        if (editor == null)
            editor = text.gameObject.AddComponent<ReadableBookTextEditor>();
        editor.Initialize(this, text, multiline);
        return editor;
    }

    #endregion

    #region 页面与物品交互

    private void BindInputInventory()
    {
        if (book?.InputInventoryData == null)
            return;

        bookInventory ??= new Inventory();
        bookInventory.item = actor;
        bookInventory.Data = book.InputInventoryData;
        bookInventory.DefaultTarget_Inventory = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory ??
                                                Inventory_Hand.PlayerHand;
        bookInventory.itemSlot_UI.Clear();
        bookInventory.InitData();
        bookInventory.BindSlotUI(writingMaterialSlot, 0);
        bookInventory.BindSlotUI(paperSlot, 1);

        book.InputInventoryData.Event_OnDataChanged -= OnInputInventoryChanged;
        book.InputInventoryData.Event_OnDataChanged += OnInputInventoryChanged;
    }

    private void PreviousSpread()
    {
        if (book == null)
            return;

        FinishTextEditing();
        spreadStart = Mathf.Max(0, spreadStart - 2);
        Refresh();
    }

    private void NextSpread()
    {
        if (book == null)
            return;

        FinishTextEditing();
        int lastSpreadStart = Mathf.Max(0, ((book.PageCount - 1) / 2) * 2);
        spreadStart = Mathf.Min(lastSpreadStart, spreadStart + 2);
        Refresh();
    }

    private void AddPaperPage()
    {
        if (book == null || !book.TryAddPageFromPaper())
            return;

        Refresh();
    }

    private void Refresh()
    {
        if (book == null)
            return;

        titleText.text = ResolveTitle();
        SetPage(leftPageNumber, leftTitleEditor, leftBodyEditor, spreadStart);
        SetPage(rightPageNumber, rightTitleEditor, rightBodyEditor, spreadStart + 1);
        previousButton.interactable = spreadStart > 0;
        nextButton.interactable = spreadStart + 2 < book.PageCount;
        addPaperPageButton.interactable = book.CanAddPage;
        writingMaterialLabel.text = FlatWorldLocalizationService.GetUiText("书写材料");
        paperLabel.text = FlatWorldLocalizationService.GetUiText("纸张");
        addPaperPageLabel.text = FlatWorldLocalizationService.GetUiText("扩充一页");
        leftTitleEditor.SetEditingAllowed(book.CanEditContent && spreadStart < book.PageCount);
        leftBodyEditor.SetEditingAllowed(book.CanEditContent && spreadStart < book.PageCount);
        rightTitleEditor.SetEditingAllowed(book.CanEditContent && spreadStart + 1 < book.PageCount);
        rightBodyEditor.SetEditingAllowed(book.CanEditContent && spreadStart + 1 < book.PageCount);
        writingMaterialSlot.RefreshUI();
        paperSlot.RefreshUI();
    }

    private void SetPage(
        TextMeshProUGUI number,
        ReadableBookTextEditor titleEditor,
        ReadableBookTextEditor bodyEditor,
        int index)
    {
        if (index >= book.PageCount)
        {
            titleEditor.BindPage(index, string.Empty, true);
            bodyEditor.BindPage(index, string.Empty, false);
            number.text = string.Empty;
            return;
        }

        string defaultTitle = string.Empty;
        string defaultBody = string.Empty;
        if (book.TryGetPage(index, out ReadableBookPageDefinition page))
        {
            string localized = FlatWorldLocalizationService.Get(page.key, page.fallback);
            SplitPageText(localized, out defaultTitle, out defaultBody);
        }

        book.ResolvePageContent(index, defaultTitle, defaultBody, out string title, out string text);
        titleEditor.BindPage(index, title, true);
        bodyEditor.BindPage(index, text, false);
        number.text = (index + 1).ToString();
    }

    private static void SplitPageText(string value, out string title, out string body)
    {
        string content = value ?? string.Empty;
        int divider = content.IndexOf("\n\n", StringComparison.Ordinal);
        if (divider >= 0)
        {
            title = content.Substring(0, divider).TrimEnd('\r', '\n');
            body = content.Substring(divider + 2).TrimStart('\r', '\n');
            return;
        }

        int firstLine = content.IndexOf('\n');
        if (firstLine >= 0)
        {
            title = content.Substring(0, firstLine).TrimEnd('\r');
            body = content.Substring(firstLine + 1).TrimStart('\r', '\n');
            return;
        }

        title = content;
        body = string.Empty;
    }

    private string ResolveTitle()
    {
        string itemId = book?.item?.itemData?.IDName;
        return GameRes.Instance != null && GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition)
            ? definition.DisplayName
            : FlatWorldLocalizationService.GetUiText("书籍");
    }

    internal bool CanEditPage => book != null && book.CanEditContent;

    internal void BeginPageEditing(ReadableBookTextEditor editor)
    {
        if (editor == null || book == null || !book.CanEditContent || !book.CanRead(actor))
            return;

        FinishTextEditing(editor);
        editor.BeginEditing();
    }

    internal void CommitPageText(int index, bool isTitle, string value)
    {
        if (book == null)
            return;

        if (isTitle)
            book.TrySetPageTitle(index, value);
        else
            book.TrySetPageBody(index, value);

        Refresh();
    }

    private void FinishTextEditing(ReadableBookTextEditor except = null)
    {
        if (leftTitleEditor != except) leftTitleEditor?.FinishEditing();
        if (leftBodyEditor != except) leftBodyEditor?.FinishEditing();
        if (rightTitleEditor != except) rightTitleEditor?.FinishEditing();
        if (rightBodyEditor != except) rightBodyEditor?.FinishEditing();
    }

    private void OnInputInventoryChanged(ItemSlot _)
    {
        Refresh();
    }

    #endregion

    #region 本地化与清理

    private void OnLanguageChanged(string _)
    {
        if (panel != null && panel.IsOpen())
            Refresh();
    }

    private void OnPanelClosed()
    {
        FinishTextEditing();
        ClearTarget();
    }

    private void ClearTarget()
    {
        if (book?.InputInventoryData != null)
            book.InputInventoryData.Event_OnDataChanged -= OnInputInventoryChanged;

        book = null;
        actor = null;
        spreadStart = 0;
    }

    private void OnDestroy()
    {
        if (panel != null)
            panel.Closed -= OnPanelClosed;
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
        FinishTextEditing();
        ClearTarget();
        if (current == this)
            current = null;
    }

    #endregion
}

/// <summary>标题和正文共用的双击编辑桥接；两次点击需在0.4秒、36像素内，鼠标与手机共用 PointerClick。</summary>
public sealed class ReadableBookTextEditor : MonoBehaviour, IPointerClickHandler
{
    #region 字段与绑定

    private const float DoubleClickSeconds = 0.4f;
    private const float DoubleClickDistance = 36f;
    private ReadableBookPanel owner;
    private TextMeshProUGUI displayText;
    private TMP_InputField inputField;
    private int pageIndex;
    private bool isTitle;
    private bool editingAllowed;
    private bool isEditing;
    private float lastClickTime = -1f;
    private Vector2 lastClickPosition;

    /// <summary>绑定页面显示文本、输入框和手机软键盘行为。</summary>
    public void Initialize(ReadableBookPanel panel, TextMeshProUGUI text, bool multiline)
    {
        owner = panel;
        displayText = text;
        displayText.raycastTarget = true;
        bool wasActive = gameObject.activeSelf;
        inputField = GetComponent<TMP_InputField>();
        if (inputField == null)
        {
            if (wasActive)
                gameObject.SetActive(false);
            inputField = gameObject.AddComponent<TMP_InputField>();
        }

        inputField.targetGraphic = displayText;
        inputField.textViewport = displayText.rectTransform;
        inputField.textComponent = displayText;
        inputField.lineType = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;
        inputField.contentType = TMP_InputField.ContentType.Standard;
        inputField.caretColor = displayText.color;
        inputField.caretWidth = 2;
        inputField.shouldHideMobileInput = false;
        inputField.shouldHideSoftKeyboard = false;
        inputField.onEndEdit.RemoveListener(OnEndEdit);
        inputField.onEndEdit.AddListener(OnEndEdit);
        inputField.enabled = false;
        if (wasActive && !gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    /// <summary>绑定当前页码与标题/正文类型，避免覆盖正在编辑的输入。</summary>
    public void BindPage(int index, string value, bool title)
    {
        pageIndex = index;
        isTitle = title;
        if (inputField != null && !isEditing)
            inputField.SetTextWithoutNotify(value ?? string.Empty);
    }

    /// <summary>按书写材料和页数控制双击编辑资格。</summary>
    public void SetEditingAllowed(bool allowed)
    {
        editingAllowed = allowed;
        if (!allowed)
            FinishEditing();
    }

    #endregion

    #region 双击与输入

    /// <summary>鼠标与触屏连续双击同一文本后开始编辑。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left ||
            !editingAllowed || isEditing)
        {
            lastClickTime = -1f;
            return;
        }

        float now = Time.unscaledTime;
        bool isDoubleClick = lastClickTime >= 0f && now - lastClickTime <= DoubleClickSeconds &&
                             (eventData.position - lastClickPosition).sqrMagnitude <=
                             DoubleClickDistance * DoubleClickDistance;
        lastClickTime = isDoubleClick ? -1f : now;
        lastClickPosition = eventData.position;
        if (isDoubleClick)
            owner.BeginPageEditing(this);
    }

    /// <summary>启用输入框并定位光标到当前文字。</summary>
    public void BeginEditing()
    {
        if (!editingAllowed || isEditing || inputField == null)
            return;

        isEditing = true;
        inputField.enabled = true;
        inputField.SetTextWithoutNotify(displayText.text);
        inputField.Select();
        inputField.ActivateInputField();
    }

    /// <summary>结束输入并将内容提交到所属书页。</summary>
    public void FinishEditing()
    {
        if (!isEditing || inputField == null)
            return;

        inputField.DeactivateInputField();
        if (isEditing)
            CommitAndDisable(inputField.text);
    }

    private void OnEndEdit(string value)
    {
        if (isEditing)
            CommitAndDisable(value);
    }

    private void CommitAndDisable(string value)
    {
        isEditing = false;
        inputField.enabled = false;
        owner?.CommitPageText(pageIndex, isTitle, value);
    }

    private void OnDestroy()
    {
        if (inputField != null)
            inputField.onEndEdit.RemoveListener(OnEndEdit);
        owner = null;
        displayText = null;
    }

    #endregion
}
