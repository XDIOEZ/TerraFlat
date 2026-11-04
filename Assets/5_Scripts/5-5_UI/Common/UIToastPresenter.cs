using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>UI 根内嵌的非交互居中提示，暂停游戏时仍按真实时间自动隐藏。</summary>
[DisallowMultipleComponent]
public sealed class UIToastPresenter : MonoBehaviour, IUiRootStaticContent
{
    #region 提示展示与生命周期

    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private CanvasGroup visibility;
    private Coroutine hideCoroutine;

    public bool IsVisible => visibility.alpha > 0f;
    public string Message => messageText.text;

    // 连续提示复用同一节点，并从最后一次提示重新计时。
    public void ShowToast(string message, float durationSeconds = 2.5f)
    {
        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);
        messageText.text = message;
        visibility.alpha = 1f;
        hideCoroutine = StartCoroutine(HideToastAfterRealtimeDelay(durationSeconds));
    }

    private IEnumerator HideToastAfterRealtimeDelay(float durationSeconds)
    {
        yield return new WaitForSecondsRealtime(durationSeconds);
        visibility.alpha = 0f;
        hideCoroutine = null;
    }

    private void OnDisable()
    {
        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);
        hideCoroutine = null;
        visibility.alpha = 0f;
    }

    #endregion
}
