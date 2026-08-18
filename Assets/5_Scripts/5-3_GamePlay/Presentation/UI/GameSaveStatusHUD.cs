using System.Collections;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// 负责显示游戏保存状态和短暂玩法提示的右上角非交互 HUD。
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

    private GameObject viewObject;
    private RectTransform viewRect;
    private CanvasGroup viewCanvasGroup;
    private TextMeshProUGUI statusText;
    private Coroutine hideCoroutine;
    private float shownAt;
    private bool saveInProgress;
    private bool saveFailed;
    private bool transientMessageActive;
    private string transientMessage;
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
        transientMessageActive = false;
        transientMessage = null;
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

        ScheduleHide(SuccessMinimumVisibleSeconds);
    }

    private void HandleLanguageChanged(string _)
    {
        if (statusText == null)
            return;

        if (saveInProgress)
            statusText.text = FlatWorldLocalizationService.GetUiText("正在保存…");
        else if (transientMessageActive)
            statusText.text = FlatWorldLocalizationService.GetUiText(transientMessage);
        else if (saveFailed)
            statusText.text = FlatWorldLocalizationService.GetUiText("保存失败");
    }

    /// <summary>显示一条复用现有状态卡片的短暂玩法提示，避免新增单句提示预制体。</summary>
    public void ShowTransientMessage(string message, float visibleSeconds = 2f)
    {
        if (string.IsNullOrWhiteSpace(message) || saveInProgress)
            return;

        CancelHideCoroutine();
        BeginViewIfReady();
        if (statusText == null)
            return;

        transientMessage = message;
        transientMessageActive = true;
        statusText.text = FlatWorldLocalizationService.GetUiText(message);
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

        if (saveInProgress && statusText != null && !viewObject.activeSelf)
        {
            statusText.text = FlatWorldLocalizationService.GetUiText("正在保存…");
            shownAt = Time.unscaledTime;
            SetViewVisible(true);
        }
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

        transientMessageActive = false;
        transientMessage = null;
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
