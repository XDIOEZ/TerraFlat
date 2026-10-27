using UnityEngine;

/// <summary>
/// 按主菜单安全区的可用宽高收放标题、四个旅程按钮和设置入口。
/// 旅程按钮采用紧凑的设计比例，用户界面倍率仍由根 CanvasScaler 统一控制。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class MainMenuResponsiveLayout : MonoBehaviour
{
    #region 参考排版

    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
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
        UIUserSettings.Changed -= ApplyLayout;
        UIUserSettings.Changed += ApplyLayout;
        CaptureReferencePositions();
        ApplyLayout();
    }

    private void OnDisable()
    {
        UIUserSettings.Changed -= ApplyLayout;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplyLayout();
    }

    #endregion

    #region 自适应排版

    /// <summary>只在屏幕、安全区或用户界面缩放发生变化时计算布局。</summary>
    private void ApplyLayout()
    {
        if (isApplying || !hasReferencePositions || brand == null || menuCard == null || settingsButton == null)
            return;

        Vector2 size = ((RectTransform)transform).rect.size;
        if (size.x <= 0f || size.y <= 0f)
            return;

        // 边距也按较紧的屏幕维度缩小，避免高 UI 缩放时边距反而占去大部分画面。
        float edgeScale = Mathf.Min(1f, Mathf.Min(size.x / ReferenceWidth, size.y / ReferenceHeight));
        float left = brandReferencePosition.x * edgeScale;
        float right = -settingsReferencePosition.x * edgeScale;
        float top = -brandReferencePosition.y * edgeScale;
        float bottom = menuReferencePosition.y * edgeScale;
        float gap = MinimumGap * edgeScale;

        float verticalScale = (size.y - top - bottom - gap) /
                              (brand.rect.height + menuCard.rect.height * JourneyCardDesignScale);
        float horizontalScale = (size.x - left - right - gap) / (brand.rect.width + settingsButton.rect.width);
        float cardWidthScale = (size.x - left - right) / (menuCard.rect.width * JourneyCardDesignScale);
        float scale = Mathf.Clamp(Mathf.Min(verticalScale, Mathf.Min(horizontalScale, cardWidthScale)), 0.01f, 1f);

        isApplying = true;
        try
        {
            brand.anchoredPosition = new Vector2(left, -top);
            menuCard.anchoredPosition = new Vector2(left, bottom);
            settingsButton.anchoredPosition = new Vector2(-right, settingsReferencePosition.y * edgeScale);

            Vector3 uniformScale = new Vector3(scale, scale, 1f);
            brand.localScale = uniformScale;
            // 仅收紧四个主菜单入口，不对根画布的用户缩放倍率重复加权。
            menuCard.localScale = new Vector3(scale * JourneyCardDesignScale,
                scale * JourneyCardDesignScale, 1f);
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
