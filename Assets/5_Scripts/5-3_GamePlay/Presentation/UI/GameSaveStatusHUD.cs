using System.Collections;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// 负责显示游戏保存状态和短暂玩法提示的顶部居中非交互 HUD。
/// 视觉节点只来自 UI_SaveStatus Prefab；保存期间使用未缩放时间保持提示和淡出不受暂停影响。
/// </summary>
[DisallowMultipleComponent]
public sealed class GameSaveStatusHUD : MonoBehaviour
{
    #region 常量与运行时状态

    public const string ViewName = "GameSaveStatusHUD";

    private const string StatusTextNodeName = "保存状态文本";
    private const float SuccessMinimumVisibleSeconds = 0.35f;
    private const float FailureVisibleSeconds = 2f;
    private const float ResourceReloadSuccessVisibleSeconds = 1.5f;
    private const float ResourceReloadFailureVisibleSeconds = 3f;

    private GameObject viewObject;
    private RectTransform viewRect;
    private CanvasGroup viewCanvasGroup;
    private TextMeshProUGUI statusText;
    private Coroutine hideCoroutine;
    private float shownAt;
    private bool saveInProgress;
    private bool saveFailed;
    private bool resourceReloadInProgress;
    private float resourceReloadProgress;
    private bool transientMessageActive;
    private string transientMessage;
    private object[] transientMessageArguments;
    private string queuedResourceReloadMessage;
    private object[] queuedResourceReloadMessageArguments;
    private float queuedResourceReloadVisibleSeconds;
    private bool missingPrefabLogged;

    #endregion

    #region 生命周期

    /// <summary>确保 GameManager 只持有一个保存状态 HUD 控制器。</summary>
    public static GameSaveStatusHUD Ensure(GameManager gameManager)
    {
        if (gameManager == null)
            return null;

        GameSaveStatusHUD hud = gameManager.GetComponent<GameSaveStatusHUD>();
        if (hud == null)
            hud = gameManager.gameObject.AddComponent<GameSaveStatusHUD>();

        return hud;
    }

    private void OnEnable()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        if (viewObject != null)
            Destroy(viewObject);
    }

    private void Update()
    {
        // GameRes/UIRoot 可能晚于 GameManager 初始化；保存期间持续尝试一次轻量接线，避免丢失提示。
        if (saveInProgress && (viewObject == null || statusText == null))
            BeginViewIfReady();
    }

    #endregion

    #region 保存状态

    /// <summary>显示“正在保存”状态并记录提示开始时间。</summary>
    public void BeginSave()
    {
        saveInProgress = true;
        saveFailed = false;
        transientMessageActive = false;
        transientMessage = null;
        transientMessageArguments = null;
        CancelHideCoroutine();
        BeginViewIfReady();
        if (statusText == null)
            return;

        statusText.text = FlatWorldLocalizationService.GetUiText("正在保存…");
        shownAt = Time.unscaledTime;
        SetViewVisible(true);
    }

    /// <summary>保存结束后短暂保留成功提示，失败时明确提示玩家。</summary>
    public void EndSave(bool succeeded)
    {
        saveInProgress = false;
        saveFailed = !succeeded;

        if (succeeded && !string.IsNullOrWhiteSpace(queuedResourceReloadMessage))
        {
            ShowQueuedResourceReloadMessage();
            return;
        }

        transientMessageActive = false;
        transientMessage = null;
        transientMessageArguments = null;
        BeginViewIfReady();
        if (statusText == null)
            return;

        if (saveFailed)
        {
            statusText.text = FlatWorldLocalizationService.GetUiText("保存失败");
            shownAt = Time.unscaledTime;
            SetViewVisible(true);
            ScheduleHide(FailureVisibleSeconds);
            return;
        }

        if (resourceReloadInProgress)
        {
            ShowResourceReloadStatus();
            return;
        }

        ScheduleHide(SuccessMinimumVisibleSeconds);
    }

    /// <summary>开始显示非阻塞的游戏内资源更新状态。</summary>
    public void BeginResourceReload()
    {
        resourceReloadInProgress = true;
        resourceReloadProgress = 0f;
        queuedResourceReloadMessage = null;
        queuedResourceReloadMessageArguments = null;
        queuedResourceReloadVisibleSeconds = 0f;
        transientMessageActive = false;
        transientMessage = null;
        transientMessageArguments = null;
        saveFailed = false;
        CancelHideCoroutine();
        BeginViewIfReady();
        if (!saveInProgress)
            ShowResourceReloadStatus();
    }

    /// <summary>按已完成比例更新资源提示；保存提示优先显示。</summary>
    public void UpdateResourceReloadProgress(float progress)
    {
        if (!resourceReloadInProgress)
            return;

        resourceReloadProgress = Mathf.Clamp01(progress);
        if (saveInProgress || saveFailed)
            return;

        BeginViewIfReady();
        ShowResourceReloadStatus();
    }

    /// <summary>显示热更新成功或失败结果；若保存提示正在显示则排队到保存提示结束后。</summary>
    public void EndResourceReload(bool succeeded, bool published = false, string failureReason = null)
    {
        resourceReloadInProgress = false;
        resourceReloadProgress = succeeded ? 1f : resourceReloadProgress;
        string message = GetResourceReloadResultSource(succeeded, published);
        object[] arguments = succeeded ? null : new object[]
            { string.IsNullOrWhiteSpace(failureReason) ? "未提供失败原因，请查看日志。" : failureReason };
        float visibleSeconds = succeeded
            ? ResourceReloadSuccessVisibleSeconds
            : ResourceReloadFailureVisibleSeconds;

        if (saveInProgress)
        {
            queuedResourceReloadMessage = message;
            queuedResourceReloadMessageArguments = arguments;
            queuedResourceReloadVisibleSeconds = visibleSeconds;
            return;
        }

        ShowTransientMessage(message, visibleSeconds, arguments);
    }

    // 提示按正式发布边界说明当前状态，并保留实际失败阶段。
    internal static string GetResourceReloadResultText(bool succeeded, bool published, string failureReason)
        => GetMessageText(GetResourceReloadResultSource(succeeded, published), succeeded ? null :
            new object[] { string.IsNullOrWhiteSpace(failureReason) ? "未提供失败原因，请查看日志。" : failureReason });

    private static string GetResourceReloadResultSource(bool succeeded, bool published)
        => succeeded ? "资源更新完成" : published ? "新资源已发布，但后续更新未完成：{0}" : "资源更新失败，仍使用旧资源：{0}";

    private static string GetMessageText(string source, object[] arguments)
    {
        if (arguments == null) return FlatWorldLocalizationService.GetUiText(source);
        var localized = new object[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
            localized[i] = arguments[i] is string text ? FlatWorldLocalizationService.GetUiText(text) : arguments[i];
        return FlatWorldLocalizationService.GetUiFormat(source, localized);
    }

    /// <summary>取消热更新提示；退出世界时不留下错误消息或拦截界面。</summary>
    public void CancelResourceReload()
    {
        resourceReloadInProgress = false;
        resourceReloadProgress = 0f;
        queuedResourceReloadMessage = null;
        queuedResourceReloadMessageArguments = null;
        queuedResourceReloadVisibleSeconds = 0f;
        CancelHideCoroutine();
        if (!saveInProgress)
            SetViewVisible(false);
    }

    private void HandleLanguageChanged(string _)
    {
        if (statusText == null)
            return;

        if (saveInProgress)
            statusText.text = FlatWorldLocalizationService.GetUiText("正在保存…");
        else if (resourceReloadInProgress)
            statusText.text = GetResourceReloadStatusText();
        else if (transientMessageActive)
            statusText.text = GetMessageText(transientMessage, transientMessageArguments);
        else if (saveFailed)
            statusText.text = FlatWorldLocalizationService.GetUiText("保存失败");
    }

    /// <summary>显示一条复用现有状态文本的短暂玩法提示，避免新增单句提示预制体。</summary>
    public void ShowTransientMessage(string message, float visibleSeconds = 2f, object[] arguments = null)
    {
        if (string.IsNullOrWhiteSpace(message) || saveInProgress || resourceReloadInProgress)
            return;

        saveFailed = false;
        CancelHideCoroutine();
        BeginViewIfReady();
        if (statusText == null)
            return;

        transientMessage = message;
        transientMessageArguments = arguments;
        transientMessageActive = true;
        statusText.text = GetMessageText(message, arguments);
        shownAt = Time.unscaledTime;
        SetViewVisible(true);
        ScheduleHide(visibleSeconds);
    }

    #endregion

    #region 视图管理

    private void BeginViewIfReady()
    {
        if (!EnsureView())
            return;

        if (statusText != null && !viewObject.activeSelf)
        {
            if (saveInProgress)
            {
                statusText.text = FlatWorldLocalizationService.GetUiText("正在保存…");
                shownAt = Time.unscaledTime;
                SetViewVisible(true);
            }
            else if (resourceReloadInProgress)
            {
                ShowResourceReloadStatus();
            }
        }
    }

    /// <summary>显示当前资源更新百分比，不接收射线也不改变世界输入状态。</summary>
    private void ShowResourceReloadStatus()
    {
        if (!resourceReloadInProgress || saveInProgress || statusText == null)
            return;

        string message = GetResourceReloadStatusText();
        if (!string.Equals(statusText.text, message, System.StringComparison.Ordinal))
            statusText.text = message;
        shownAt = Time.unscaledTime;
        SetViewVisible(true);
    }

    private string GetResourceReloadStatusText()
    {
        return FlatWorldLocalizationService.GetUiFormat(
            "资源更新中 {0:0}%",
            resourceReloadProgress * 100f);
    }

    private void ShowQueuedResourceReloadMessage()
    {
        string message = queuedResourceReloadMessage;
        object[] arguments = queuedResourceReloadMessageArguments;
        float visibleSeconds = queuedResourceReloadVisibleSeconds;
        queuedResourceReloadMessage = null;
        queuedResourceReloadMessageArguments = null;
        queuedResourceReloadVisibleSeconds = 0f;
        saveFailed = false;
        ShowTransientMessage(message, visibleSeconds, arguments);
    }

    private bool EnsureView()
    {
        Transform panelRoot = UIManager.Instance?.panelRoot;
        RectTransform rootRect = panelRoot as RectTransform ?? panelRoot?.GetComponent<RectTransform>();
        if (rootRect == null)
            return false;

        if (viewObject != null)
        {
            if (viewRect != null && viewRect.parent != rootRect)
                viewRect.SetParent(rootRect, false);

            viewRect?.SetAsLastSibling();
            return viewCanvasGroup != null && statusText != null;
        }

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.SaveStatus, false);
        if (prefab == null)
        {
            if (!missingPrefabLogged && GameRes.Instance != null)
            {
                Debug.LogError("[GameSaveStatusHUD] 缺少 UI_SaveStatus Prefab。", this);
                missingPrefabLogged = true;
            }

            return false;
        }

        viewObject = Instantiate(prefab, rootRect, false);
        viewObject.name = ViewName;
        viewRect = viewObject.GetComponent<RectTransform>();
        viewCanvasGroup = viewObject.GetComponent<CanvasGroup>();
        Transform statusNode = FindChildRecursive(viewObject.transform, StatusTextNodeName);
        statusText = statusNode?.GetComponent<TextMeshProUGUI>();

        if (viewRect == null || viewCanvasGroup == null || statusText == null)
        {
            Debug.LogError("[GameSaveStatusHUD] UI_SaveStatus Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            viewCanvasGroup = null;
            statusText = null;
            return false;
        }

        viewRect.SetAsLastSibling();
        SetViewVisible(false);
        return true;
    }

    private void SetViewVisible(bool visible)
    {
        if (viewObject == null || viewCanvasGroup == null)
            return;

        viewCanvasGroup.alpha = visible ? 1f : 0f;
        viewCanvasGroup.interactable = false;
        viewCanvasGroup.blocksRaycasts = false;
        if (viewObject.activeSelf != visible)
            viewObject.SetActive(visible);
    }

    private void ScheduleHide(float visibleSeconds)
    {
        CancelHideCoroutine();
        hideCoroutine = StartCoroutine(HideAfterRealtime(visibleSeconds));
    }

    private IEnumerator HideAfterRealtime(float visibleSeconds)
    {
        float remaining = Mathf.Max(0f, visibleSeconds - (Time.unscaledTime - shownAt));
        if (remaining > 0f)
            yield return new WaitForSecondsRealtime(remaining);

        hideCoroutine = null;
        if (saveInProgress)
            yield break;

        if (resourceReloadInProgress)
        {
            saveFailed = false;
            ShowResourceReloadStatus();
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(queuedResourceReloadMessage))
        {
            ShowQueuedResourceReloadMessage();
            yield break;
        }

        transientMessageActive = false;
        transientMessage = null;
        transientMessageArguments = null;
        saveFailed = false;
        SetViewVisible(false);
    }

    private void CancelHideCoroutine()
    {
        if (hideCoroutine == null)
            return;

        StopCoroutine(hideCoroutine);
        hideCoroutine = null;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
                return children[i];
        }

        return null;
    }

    #endregion
}
