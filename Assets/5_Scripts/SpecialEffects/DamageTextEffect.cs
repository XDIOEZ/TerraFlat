using System;
using TMPro;
using UnityEngine;

/// <summary>伤害数字的视觉分类，调用方可按实际伤害类型选择颜色和弹性倍率。</summary>
public enum DamageTextStyle
{
    Normal,
    Critical,
    Cutting,
    Blunt,
    Piercing,
    Fire,
    Ice,
    Poison,
    Heal
}

/// <summary>伤害数字播放数据；保留 float 传参兼容旧攻击特效配置。</summary>
public struct DamageTextEffectData
{
    public float Value;
    public DamageTextStyle Style;
    public bool UseColorOverride;
    public Color ColorOverride;
    public float ScaleMultiplier;

    public DamageTextEffectData(float value, DamageTextStyle style = DamageTextStyle.Normal)
    {
        Value = value;
        Style = style;
        UseColorOverride = false;
        ColorOverride = Color.white;
        ScaleMultiplier = 1f;
    }
}

/// <summary>
/// 弹性伤害数字特效。
/// 使用 Update 驱动而不是每次创建协程，配合 GameEffect 的回收回调复用实例；暴击、伤害类型和颜色覆盖都由数据驱动。
/// </summary>
public sealed class DamageTextEffect : GameEffect
{
    #region Inspector

    [Header("文本设置")]
    public TMP_Text TMP_text;
    [Min(0f)] public float moveSpeed = 1.0f;
    [Min(0.05f)] public float fadeDuration = 1.0f;
    [Min(0f)] public float randomRange = 0.5f;
    public Vector2 moveDirection = Vector2.up;

    [Header("弹性表现")]
    [Min(0.01f)] public float appearDuration = 0.18f;
    [Min(1f)] public float criticalScale = 1.35f;

    [Header("伤害类型颜色")]
    public Color normalColor = new Color(1f, 0.32f, 0.22f, 1f);
    public Color criticalColor = new Color(1f, 0.86f, 0.2f, 1f);
    public Color cuttingColor = new Color(1f, 0.55f, 0.25f, 1f);
    public Color bluntColor = new Color(0.85f, 0.85f, 0.9f, 1f);
    public Color piercingColor = new Color(0.75f, 0.55f, 1f, 1f);
    public Color fireColor = new Color(1f, 0.2f, 0.06f, 1f);
    public Color iceColor = new Color(0.25f, 0.82f, 1f, 1f);
    public Color poisonColor = new Color(0.55f, 1f, 0.25f, 1f);
    public Color healColor = new Color(0.25f, 1f, 0.45f, 1f);

    #endregion

    #region Runtime State

    private float lifetime;
    private Color baseTextColor = Color.white;
    private Color activeTextColor = Color.white;
    private Vector3 startPosition;
    private Vector3 targetPosition;
    private Vector3 baseLocalScale = Vector3.one;
    private Vector3 targetLocalScale = Vector3.one;
    private bool hasCachedText;
    private bool isAnimating;

    #endregion

    #region Lifecycle

    private void Awake()
    {
        CacheText();
        baseLocalScale = transform.localScale;
    }

    private void Update()
    {
        if (!isAnimating || TMP_text == null)
            return;

        lifetime += Time.deltaTime;
        float duration = Mathf.Max(0.05f, fadeDuration);
        float progress = Mathf.Clamp01(lifetime / duration);

        transform.position = Vector3.LerpUnclamped(
            startPosition,
            targetPosition,
            EaseOutCubic(progress));

        float appearProgress = Mathf.Clamp01(lifetime / Mathf.Max(0.01f, appearDuration));
        float scaleProgress = EaseOutBack(appearProgress);
        transform.localScale = Vector3.LerpUnclamped(Vector3.zero, targetLocalScale, scaleProgress);

        float fadeProgress = Mathf.InverseLerp(0.35f, 1f, progress);
        float alpha = Mathf.Lerp(baseTextColor.a, 0f, fadeProgress);
        TMP_text.color = new Color(activeTextColor.r, activeTextColor.g, activeTextColor.b, alpha);

        if (lifetime >= duration)
        {
            isAnimating = false;
            ReturnToPoolOrDestroy();
        }
    }

    #endregion

    #region Pool Lifecycle

    public override void OnSpawnedFromPool()
    {
        CacheText();
        StopAnimationAndReset();
    }

    public override void OnReturnedToPool()
    {
        StopAnimationAndReset();
    }

    #endregion

    #region Effect API

    /// <summary>开始播放伤害数字；旧调用传入 float 时使用普通伤害样式。</summary>
    public override void Effect(Transform sender, object data = null)
    {
        if (!CacheText())
        {
            Debug.LogError("DamageTextEffect: TMP_Text 组件未找到！", this);
            ReturnToPoolOrDestroy();
            return;
        }

        StopAnimationAndReset();

        DamageTextEffectData effectData = ParseData(data);
        TMP_text.SetText("{0:0}", effectData.Value);

        Color styleColor = effectData.UseColorOverride
            ? effectData.ColorOverride
            : GetStyleColor(effectData.Style);
        activeTextColor = new Color(styleColor.r, styleColor.g, styleColor.b, baseTextColor.a);
        TMP_text.color = activeTextColor;

        Vector3 direction = moveDirection.sqrMagnitude > 0.0001f
            ? (Vector3)moveDirection.normalized
            : Vector3.up;
        Vector3 randomOffset = new Vector3(
            UnityEngine.Random.Range(-randomRange, randomRange),
            UnityEngine.Random.Range(-randomRange, randomRange),
            0f);

        startPosition = transform.position;
        Vector3 senderPosition = sender != null ? sender.position : startPosition;
        if (startPosition == Vector3.zero && sender != null)
            startPosition = senderPosition;

        targetPosition = startPosition + direction * (moveSpeed * Mathf.Max(0.05f, fadeDuration)) + randomOffset;
        float styleScale = effectData.Style == DamageTextStyle.Critical ? criticalScale : 1f;
        float dataScale = Mathf.Max(0.1f, effectData.ScaleMultiplier <= 0f ? 1f : effectData.ScaleMultiplier);
        targetLocalScale = baseLocalScale * styleScale * dataScale;
        isAnimating = true;
    }

    #endregion

    #region Helpers

    /// <summary>缓存 TMP 并保存池化前的基础颜色。</summary>
    private bool CacheText()
    {
        if (TMP_text == null)
            TMP_text = GetComponentInChildren<TMP_Text>(true);

        if (TMP_text == null)
            return false;

        if (!hasCachedText)
        {
            baseTextColor = TMP_text.color;
            hasCachedText = true;
        }

        return true;
    }

    /// <summary>重置动画状态，保证同一个池实例不会继承上一次的透明度或缩放。</summary>
    private void StopAnimationAndReset()
    {
        isAnimating = false;
        lifetime = 0f;
        transform.localScale = baseLocalScale;
        activeTextColor = baseTextColor;
        targetLocalScale = baseLocalScale;

        if (TMP_text != null)
            TMP_text.color = baseTextColor;
    }

    /// <summary>兼容旧 float 数据并解析新的样式数据。</summary>
    private static DamageTextEffectData ParseData(object data)
    {
        if (data is DamageTextEffectData effectData)
            return effectData;

        if (data == null)
            return new DamageTextEffectData(0f);

        try
        {
            return new DamageTextEffectData(System.Convert.ToSingle(data));
        }
        catch (Exception)
        {
            return new DamageTextEffectData(0f);
        }
    }

    /// <summary>按伤害样式选择默认颜色。</summary>
    private Color GetStyleColor(DamageTextStyle style)
    {
        switch (style)
        {
            case DamageTextStyle.Critical:
                return criticalColor;
            case DamageTextStyle.Cutting:
                return cuttingColor;
            case DamageTextStyle.Blunt:
                return bluntColor;
            case DamageTextStyle.Piercing:
                return piercingColor;
            case DamageTextStyle.Fire:
                return fireColor;
            case DamageTextStyle.Ice:
                return iceColor;
            case DamageTextStyle.Poison:
                return poisonColor;
            case DamageTextStyle.Heal:
                return healColor;
            default:
                return normalColor;
        }
    }

    /// <summary>平滑减速移动曲线。</summary>
    private static float EaseOutCubic(float value)
    {
        float inverse = 1f - Mathf.Clamp01(value);
        return 1f - inverse * inverse * inverse;
    }

    /// <summary>带少量过冲的弹性出现曲线。</summary>
    private static float EaseOutBack(float value)
    {
        float inverse = Mathf.Clamp01(value) - 1f;
        const float overshoot = 1.70158f;
        return inverse * inverse * ((overshoot + 1f) * inverse + overshoot) + 1f;
    }

    #endregion
}
