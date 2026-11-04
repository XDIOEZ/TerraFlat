using UnityEngine;
using UnityEngine.UI;

/// <summary>主菜单背景用两层图片交叉淡化，面板关闭时取消过渡并停止计时。</summary>
[DisallowMultipleComponent]
public sealed class MainMenuBackgroundSlideshowController : MonoBehaviour
{
    #region 配置与状态
    [SerializeField] private BasePanel menuPanel;
    [SerializeField] private Sprite[] backgrounds;
    [SerializeField] private Image currentLayer;
    [SerializeField] private Image incomingLayer;
    [SerializeField] private float holdDuration = 12f;
    [SerializeField] private float fadeDuration = 1.5f;

    private int currentIndex = -1;
    private float elapsed;
    private bool isFading;
    private bool isRunning;
    #endregion

    #region 装配与生命周期
    public void ConfigureBackgroundSlideshow(BasePanel panel, Sprite[] sprites, Image bottom, Image top)
    {
        menuPanel = panel;
        backgrounds = sprites;
        currentLayer = bottom;
        incomingLayer = top;
        SetLayerSprite(currentLayer, backgrounds[0]);
        SetLayerSprite(incomingLayer, backgrounds[0]);
        incomingLayer.enabled = false;
    }

    private void OnEnable()
    {
        if (menuPanel == null)
            return;
        menuPanel.Opened += ResumeBackgroundSlideshow;
        menuPanel.Closed += StopBackgroundSlideshow;
        if (menuPanel.IsOpen())
            ResumeBackgroundSlideshow();
    }

    private void Start()
    {
        // UIManager 在实例化后才初始化 BasePanel，首次 Start 再同步其真实开关状态。
        if (menuPanel.IsOpen())
            ResumeBackgroundSlideshow();
        else
            StopBackgroundSlideshow();
    }

    private void OnDisable()
    {
        if (menuPanel != null)
        {
            menuPanel.Opened -= ResumeBackgroundSlideshow;
            menuPanel.Closed -= StopBackgroundSlideshow;
        }
        StopBackgroundSlideshow();
    }

    private void Update() => AdvanceBackgroundSlideshow(Time.unscaledDeltaTime);

    private void ResumeBackgroundSlideshow()
    {
        if (currentIndex < 0)
        {
            currentIndex = new System.Random().Next(backgrounds.Length);
            SetLayerSprite(currentLayer, backgrounds[currentIndex]);
        }
        elapsed = 0f;
        isRunning = true;
    }

    private void StopBackgroundSlideshow()
    {
        isRunning = false;
        elapsed = 0f;
        isFading = false;
        if (incomingLayer != null)
            incomingLayer.enabled = false;
    }
    #endregion

    #region 双层交叉淡化
    private void AdvanceBackgroundSlideshow(float unscaledDeltaTime)
    {
        if (!isRunning)
            return;

        elapsed += unscaledDeltaTime;
        while (true)
        {
            if (!isFading)
            {
                if (elapsed < holdDuration)
                    return;
                elapsed -= holdDuration;
                SetLayerSprite(incomingLayer, backgrounds[(currentIndex + 1) % backgrounds.Length]);
                incomingLayer.enabled = true;
                isFading = true;
            }

            if (elapsed < fadeDuration)
            {
                incomingLayer.color = new Color(1f, 1f, 1f, elapsed / fadeDuration);
                return;
            }

            elapsed -= fadeDuration;
            incomingLayer.color = Color.white;
            (currentLayer, incomingLayer) = (incomingLayer, currentLayer);
            currentLayer.transform.SetSiblingIndex(0);
            incomingLayer.transform.SetSiblingIndex(1);
            incomingLayer.enabled = false;
            currentIndex = (currentIndex + 1) % backgrounds.Length;
            isFading = false;
        }
    }

    private static void SetLayerSprite(Image layer, Sprite sprite)
    {
        layer.sprite = sprite;
        layer.color = Color.white;
        layer.GetComponent<AspectRatioFitter>().aspectRatio = sprite.rect.width / sprite.rect.height;
    }
    #endregion
}
