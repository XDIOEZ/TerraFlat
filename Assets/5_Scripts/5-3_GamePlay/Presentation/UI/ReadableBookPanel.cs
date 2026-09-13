using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>双页书籍阅读面板。正式层级由 UI_ReadableBook Prefab 提供，本脚本只处理翻页和文本刷新。</summary>
public sealed class ReadableBookPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI leftPageText;
    [SerializeField] private TextMeshProUGUI rightPageText;
    [SerializeField] private TextMeshProUGUI leftPageNumber;
    [SerializeField] private TextMeshProUGUI rightPageNumber;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    private BasePanel panel;
    private Mod_ReadableBook book;
    private Item actor;
    private int spreadStart;
    private static ReadableBookPanel current;

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

        current.book = target;
        current.actor = owner;
        current.spreadStart = 0;
        current.Refresh();
        current.panel.Open();
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        if (panel == null)
            throw new MissingComponentException("UI_ReadableBook 缺少 BasePanel。");

        if (titleText == null || leftPageText == null || rightPageText == null ||
            leftPageNumber == null || rightPageNumber == null || previousButton == null || nextButton == null)
        {
            throw new MissingReferenceException("UI_ReadableBook 的书页或翻页控件引用未完整绑定。");
        }

        previousButton.onClick.AddListener(PreviousSpread);
        nextButton.onClick.AddListener(NextSpread);
        panel.Closed += ClearTarget;
        panel.PrepareForGamepadNavigation(nextButton.name, true, true);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>玩家切走物品、丢下读物或目标销毁时立即关闭，避免面板与手持状态脱节。</summary>
    private void Update()
    {
        if (panel.IsOpen() && (book == null || !book.CanRead(actor)))
            panel.Close();
    }

    private void PreviousSpread()
    {
        if (book == null)
            return;

        spreadStart = Mathf.Max(0, spreadStart - 2);
        Refresh();
    }

    private void NextSpread()
    {
        if (book == null)
            return;

        int lastSpreadStart = Mathf.Max(0, ((book.PageCount - 1) / 2) * 2);
        spreadStart = Mathf.Min(lastSpreadStart, spreadStart + 2);
        Refresh();
    }

    private void Refresh()
    {
        if (book == null)
            return;

        titleText.text = ResolveTitle();
        SetPage(leftPageText, leftPageNumber, spreadStart);
        SetPage(rightPageText, rightPageNumber, spreadStart + 1);
        previousButton.interactable = spreadStart > 0;
        nextButton.interactable = spreadStart + 2 < book.PageCount;
    }

    private string ResolveTitle()
    {
        string itemId = book?.item?.itemData?.IDName;
        return GameRes.Instance != null && GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition)
            ? definition.DisplayName
            : FlatWorldLocalizationService.GetUiText("书籍");
    }

    private void SetPage(TextMeshProUGUI body, TextMeshProUGUI number, int index)
    {
        if (!book.TryGetPage(index, out ReadableBookPageDefinition page))
        {
            body.text = string.Empty;
            number.text = string.Empty;
            return;
        }

        body.text = FlatWorldLocalizationService.Get(page.key, page.fallback);
        number.text = (index + 1).ToString();
    }

    private void OnLanguageChanged(string _)
    {
        if (panel != null && panel.IsOpen())
            Refresh();
    }

    private void ClearTarget()
    {
        book = null;
        actor = null;
        spreadStart = 0;
    }

    private void OnDestroy()
    {
        if (panel != null)
            panel.Closed -= ClearTarget;
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
        ClearTarget();
        if (current == this)
            current = null;
    }
}
