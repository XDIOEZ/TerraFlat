using DG.Tweening;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// 快捷栏上方的手持物名称提示，读取正式物品定义的本地化名称。
/// 切换后显示 3 秒，再用 0.8 秒淡出；连续切换会取消旧动画并重新计时，空手立即隐藏。
/// 视觉和输入透明配置由 UI_HotBar Prefab 持有，名称通过 LocalizedTextBinder 随语言切换刷新。
/// </summary>
[DisallowMultipleComponent]
public sealed class HotbarItemNameHUD : MonoBehaviour
{
    #region 视图与状态

    [SerializeField] private CanvasGroup nameGroup; // 名称底板及文字的统一透明度。
    [SerializeField] private TextMeshProUGUI nameText; // 居中的动态物品名称。
    [SerializeField, Min(0f)] private float visibleSeconds = 3f;
    [SerializeField, Min(0.1f)] private float fadeSeconds = 0.8f;

    private Inventory_HotBar hotbar;
    private LocalizedTextBinder nameBinding;
    private Tween fadeTween;

    #endregion

    #region 绑定与生命周期

    private void Awake()
    {
        nameBinding = nameText.GetComponent<LocalizedTextBinder>();
    }

    /// <summary>由本地快捷栏在 UI 初始化时绑定，远端视觉副本不创建此视图。</summary>
    public void Bind(Inventory_HotBar source)
    {
        Unsubscribe();
        hotbar = source;
        if (isActiveAndEnabled)
        {
            Subscribe();
            ShowItem(hotbar?.CurentSelectItem?.itemData);
        }
    }

    private void OnEnable()
    {
        Subscribe();
        ShowItem(hotbar?.CurentSelectItem?.itemData);
    }

    private void OnDisable()
    {
        Unsubscribe();
        Hide();
    }

    /// <summary>事件只订阅一次，不轮询快捷栏或逐帧查找物品。</summary>
    private void Subscribe()
    {
        if (hotbar != null)
            hotbar.HeldItemChanged += ShowItem;
    }

    private void Unsubscribe()
    {
        if (hotbar != null)
            hotbar.HeldItemChanged -= ShowItem;
    }

    #endregion

    #region 名称与淡出

    /// <summary>以本次手持物重置提示和计时，空槽不显示提示。</summary>
    private void ShowItem(ItemData itemData)
    {
        Hide();
        if (itemData == null)
            return;

        if (!GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
        {
            Debug.LogError($"[HotbarItemNameHUD] 物品缺少正式定义：{itemData.IDName}", this);
            return;
        }

        // 使用内容表的显式名称 key，切换语言只刷新文字，不重置动画计时。
        nameBinding.Configure(FlatWorldLocalizationService.DefaultTable, definition.LabelKey, definition.DisplayName);

        nameGroup.alpha = 1f;
        fadeTween = DOTween.To(() => nameGroup.alpha, value => nameGroup.alpha = value, 0f, fadeSeconds)
            .SetDelay(visibleSeconds)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .OnComplete(() => fadeTween = null);
    }

    /// <summary>隐藏或销毁时终止旧动画，避免淡出回调影响后续物品。</summary>
    private void Hide()
    {
        fadeTween?.Kill();
        fadeTween = null;
        nameGroup.alpha = 0f;
        nameBinding.Configure(FlatWorldLocalizationService.DefaultTable, string.Empty, string.Empty);
        nameText.text = string.Empty;
    }

    #endregion
}
