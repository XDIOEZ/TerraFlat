using UnityEngine;
using UnityEngine.UI;

/// <summary>让只读进度条的填充平滑追随真实数值。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Slider))]
[DefaultExecutionOrder(32000)]
public sealed class SmoothProgressBarVisual : MonoBehaviour
{
    #region 填充过渡

    [SerializeField, Min(0f)] private float transitionDuration = 0.24f;
    private Slider slider;
    private RectTransform fillRect;
    private Image fillImage;
    private float displayedValue;
    private float startValue;
    private float targetValue;
    private float elapsed;
    private bool initialized;

    private void Awake() => slider = GetComponent<Slider>();

    private void OnEnable() => initialized = false;

    private void LateUpdate() => AdvanceProgressTransition(Time.unscaledDeltaTime);

    public void AdvanceProgressTransition(float unscaledDeltaTime)
    {
        RectTransform currentFill = slider.fillRect;
        if (currentFill == null)
        {
            initialized = false;
            return;
        }

        if (fillRect != currentFill)
        {
            fillRect = currentFill;
            fillImage = fillRect.GetComponent<Image>();
            initialized = false;
        }

        if (fillImage != null && fillImage.type == Image.Type.Filled &&
            (fillImage.fillMethod == Image.FillMethod.Horizontal || fillImage.fillMethod == Image.FillMethod.Vertical))
        {
            bool vertical = slider.direction == Slider.Direction.BottomToTop || slider.direction == Slider.Direction.TopToBottom;
            bool reverse = slider.direction == Slider.Direction.RightToLeft || slider.direction == Slider.Direction.TopToBottom;
            Image.FillMethod method = vertical ? Image.FillMethod.Vertical : Image.FillMethod.Horizontal;
            if (fillImage.fillMethod != method)
                fillImage.fillMethod = method;
            int origin = reverse ? 1 : 0;
            if (fillImage.fillOrigin != origin)
                fillImage.fillOrigin = origin;
        }

        float nextValue = slider.normalizedValue;
        if (!initialized)
        {
            displayedValue = startValue = targetValue = nextValue;
            elapsed = transitionDuration;
            initialized = true;
            return;
        }

        if (!Mathf.Approximately(targetValue, nextValue))
        {
            startValue = displayedValue;
            targetValue = nextValue;
            elapsed = 0f;
        }
        else if (elapsed >= transitionDuration)
        {
            return;
        }

        elapsed += Mathf.Max(0f, unscaledDeltaTime);
        float progress = transitionDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / transitionDuration);
        float inverse = 1f - progress;
        displayedValue = Mathf.LerpUnclamped(startValue, targetValue, 1f - inverse * inverse * inverse);
        ApplyDisplayedFill(displayedValue);
    }

    private void ApplyDisplayedFill(float value)
    {
        if (fillImage != null && fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = value;
            return;
        }

        bool vertical = slider.direction == Slider.Direction.BottomToTop || slider.direction == Slider.Direction.TopToBottom;
        bool reverse = slider.direction == Slider.Direction.RightToLeft || slider.direction == Slider.Direction.TopToBottom;
        int axis = vertical ? 1 : 0;
        Vector2 min = fillRect.anchorMin;
        Vector2 max = fillRect.anchorMax;
        min[axis] = reverse ? 1f - value : 0f;
        max[axis] = reverse ? 1f : value;
        fillRect.anchorMin = min;
        fillRect.anchorMax = max;
    }

    #endregion
}
