using UnityEngine;

/// <summary>
/// 在统一 UI 缩放后的安全区中排列主菜单，只有内容真正放不下时才一起等比限幅。
/// 标题、四个旅程按钮和设置入口都保留根 CanvasScaler 提供的用户缩放倍率。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class MainMenuResponsiveLayout : MonoBehaviour
{
    #region 参考排版

    private const float MinimumGap = 32f;
    private const float JourneyCardDesignScale = 0.6f;

    [SerializeField] private RectTransform brand; // 标题与分隔线的共同缩放根。
    [SerializeField] private RectTransform menuCard; // 四个旅程按钮的共同缩放根。
    [SerializeField] private RectTransform settingsButton; // 右上角设置入口。

    private Vector2 brandReferencePosition;
    private Vector2 menuReferencePosition;
    private Vector2 settingsReferencePosition;
    private bool hasReferencePositions;
    private bool isApplying;

    #endregion

    #region 生命周期

    /// <summary>由正式 Prefab 与主菜单重建入口设置三个现有布局节点。</summary>
    public void Configure(RectTransform titleArea, RectTransform buttons, RectTransform settings)
    {
        brand = titleArea;
        menuCard = buttons;
        settingsButton = settings;
        hasReferencePositions = false;
        CaptureReferencePositions();
        if (Application.isPlaying)
            ApplyLayout();
    }

    private void Awake()
    {
        CaptureReferencePositions();
    }

    private void OnEnable()
    {
        UIUserSettings.Changed -= RefreshLayout;
        UIUserSettings.Changed += RefreshLayout;
        CaptureReferencePositions();
        RefreshLayout();
    }

    private void OnDisable()
    {
        UIUserSettings.Changed -= RefreshLayout;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplyLayout();
    }

    #endregion

    #region 自适应排版

    /// <summary>按当前根画布和安全区重新排列主菜单，供缩放事件和布局验证共用。</summary>
    public void RefreshLayout()
    {
        CaptureReferencePositions();
        ApplyLayout();
    }

    /// <summary>只在屏幕、安全区或用户界面缩放发生变化时计算布局。</summary>
    private void ApplyLayout()
    {
        if (isApplying || !hasReferencePositions || brand == null || menuCard == null || settingsButton == null)
            return;

        Vector2 size = ((RectTransform)transform).rect.size;
        if (size.x <= 0f || size.y <= 0f)
            return;

        float brandLeft = brandReferencePosition.x;
        float cardLeft = menuReferencePosition.x;
        float right = -settingsReferencePosition.x;
        float brandTop = -brandReferencePosition.y;
        float settingsTop = -settingsReferencePosition.y;
        float bottom = menuReferencePosition.y;

        // 以 Prefab 的完整设计尺寸计算占屏预算，不能随 Canvas 变小再次缩减边距并抵消用户倍率。
        float cardWidth = menuCard.rect.width * JourneyCardDesignScale;
        float cardHeight = menuCard.rect.height * JourneyCardDesignScale;
        float contentWidth = Mathf.Max(
            brandLeft + brand.rect.width + MinimumGap + settingsButton.rect.width + right,
            cardLeft + cardWidth + right);
        float contentHeight = Mathf.Max(brandTop + brand.rect.height,
            settingsTop + settingsButton.rect.height) + MinimumGap + cardHeight + bottom;
        float layoutScale = Mathf.Clamp(Mathf.Min(1f,
            Mathf.Min(size.x / contentWidth, size.y / contentHeight)), 0.01f, 1f);

        isApplying = true;
        try
        {
            brand.anchoredPosition = brandReferencePosition * layoutScale;
            menuCard.anchoredPosition = menuReferencePosition * layoutScale;
            settingsButton.anchoredPosition = settingsReferencePosition * layoutScale;

            Vector3 uniformScale = new Vector3(layoutScale, layoutScale, 1f);
            brand.localScale = uniformScale;
            // 旅程卡保留紧凑设计尺寸，但三个内容组始终使用同一个自适应系数。
            menuCard.localScale = new Vector3(layoutScale * JourneyCardDesignScale,
                layoutScale * JourneyCardDesignScale, 1f);
            settingsButton.localScale = uniformScale;
        }
        finally
        {
            isApplying = false;
        }
    }

    /// <summary>Prefab 上的位置是设计基准，运行时缩放不能反过来覆盖它。</summary>
    private void CaptureReferencePositions()
    {
        if (hasReferencePositions || brand == null || menuCard == null || settingsButton == null)
            return;

        brandReferencePosition = brand.anchoredPosition;
        menuReferencePosition = menuCard.anchoredPosition;
        settingsReferencePosition = settingsButton.anchoredPosition;
        hasReferencePositions = true;
    }

    #endregion
}
