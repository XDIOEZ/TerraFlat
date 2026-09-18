using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// UI 面板过渡动画基类：只处理 CanvasGroup、位移、缩放等视觉表现，不持有 BasePanel 或业务状态。
/// BasePanel 在自身 Open/Close 生命周期中直接调用本组件；AnimationId 用于从 UIAnimationManager 获取 JSON 配置。
/// 子类优先重写 CreateTween、ApplyVisualState 与姿态钩子，不需要监听任何面板事件。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[AddComponentMenu("FlatWorld/UI/Animation/Base UI Animation (Fade)")]
public class BaseUIAnimation : MonoBehaviour
{
    #region 配置与运行状态
    [SerializeField, Tooltip("稳定 JSON 配置 ID；不使用运行时 GameObject 名称匹配。")]
    private string animationId = UIAnimationManager.DefaultAnimationId;
    [SerializeField, Tooltip("滑动/缩放子类使用的独立视觉节点；不要选布局或安全区控制器驱动的节点。")]
    private RectTransform motionRoot;

    private UIAnimationManager manager;
    private UIAnimationProfile configuredProfile;
    private UIAnimationProfile playingProfile;
    private CanvasGroup group;
    private Tween activeTween;
    private Action completion;
    private int transitionRevision;
    private float visibility;
    private float targetOpenAlpha = 1f;
    private bool targetOpening;

    public string AnimationId => animationId;
    public bool IsPlaying => activeTween != null && activeTween.IsActive();
    public bool IsTransitioning => completion != null || IsPlaying;
    public float Visibility => Mathf.Clamp01(visibility);
    protected CanvasGroup Group => group;
    protected RectTransform MotionRoot => motionRoot;
    protected UIAnimationProfile Profile => playingProfile ?? configuredProfile;
    #endregion

    #region Unity 生命周期
    protected virtual void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        group = GetComponent<CanvasGroup>();
        manager = UIAnimationManager.Instance;
        manager.Register(this);
        RefreshConfiguration();
    }

    /// <summary>组件失活时立即完成当前过渡，避免所属面板停在半透明中间态。</summary>
    protected virtual void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        // 单独禁用动画组件时仍要把面板过渡补完；若整个对象/父级正在停用，则先静默取消，
        // 交给 BasePanel.OnDisable 在自身已经失活后提交终态，避免 Unity 禁止的兄弟层级修改。
        if (gameObject.activeInHierarchy)
            CompleteAnimation();
        else
            CancelAnimation();
        manager?.Unregister(this);
        manager = null;
    }

    /// <summary>销毁时只清理本组件拥有的 Tween，不触碰其它 DOTween。</summary>
    protected virtual void OnDestroy()
    {
        CancelAnimation();
        manager?.Unregister(this);
        manager = null;
    }
    #endregion

    #region BasePanel 直接调用入口
    /// <summary>播放打开动画；返回 false 表示当前不能接管，由 BasePanel 立即提交视觉终态。</summary>
    public virtual bool PlayOpen(float openAlpha, Action onCompleted)
    {
        return PlayTransition(true, openAlpha, onCompleted);
    }

    /// <summary>播放关闭动画；openAlpha 用于从当前完整可见态正确插值到透明。</summary>
    public virtual bool PlayClose(float openAlpha, Action onCompleted)
    {
        return PlayTransition(false, openAlpha, onCompleted);
    }

    /// <summary>建立唯一过渡；反向开关从当前视觉进度继续，不重置到起点。</summary>
    protected virtual bool PlayTransition(bool opening, float openAlpha, Action onCompleted)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || manager == null || !manager.AnimationsEnabled)
            return false;

        bool reversing = IsTransitioning;
        int revision = ++transitionRevision;
        targetOpening = opening;
        targetOpenAlpha = Mathf.Clamp01(openAlpha);
        completion = onCompleted;

        KillOwnedTween();
        if (!reversing)
        {
            playingProfile = configuredProfile;
            CaptureRestPose();
            visibility = opening ? 0f : 1f;
        }

        ApplyVisualState(visibility);
        float target = opening ? 1f : 0f;
        float duration = GetFullDuration(opening) * Mathf.Abs(target - visibility);
        if (duration <= 0f || !gameObject.activeInHierarchy)
        {
            FinishTransition(revision);
            return true;
        }

        activeTween = CreateTween(opening, duration);
        if (activeTween == null)
        {
            FinishTransition(revision);
            return true;
        }

        // UI 动画不受游戏暂停影响；倍率只应用到本系统拥有的根 Tween。
        activeTween.SetUpdate(UpdateType.Normal, true)
            .SetAutoKill(true)
            .SetRecyclable(false)
            .OnComplete(() => FinishTransition(revision))
            .OnKill(() => HandleTweenKilled(revision));
        ApplyPlaybackSpeed();
        activeTween.Play();
        return true;
    }
    #endregion

    #region 动画扩展点
    /// <summary>默认用归一化可见度驱动淡入淡出；子类可返回自己的根 Sequence。</summary>
    protected virtual Tween CreateTween(bool opening, float duration)
    {
        return DOTween.To(GetVisibility, SetVisibility, opening ? 1f : 0f, duration)
            .SetEase(opening ? Profile.OpenEase : Profile.CloseEase);
    }

    /// <summary>完整行程时长只来自 JSON Duration；距离由 Canvas UI 坐标和 Offset 自然适配。</summary>
    protected virtual float GetFullDuration(bool opening)
    {
        return opening ? Profile.OpenDuration : Profile.CloseDuration;
    }

    /// <summary>只在一段完整行程起点采集静止姿态，反向过程中不重复采集。</summary>
    protected virtual void CaptureRestPose() { }

    /// <summary>结束、禁用或取消后还原本组件拥有的几何属性。</summary>
    protected virtual void RestoreRestPose() { }

    /// <summary>默认只控制透明度；最终业务 Alpha 仍由 BasePanel 在完成回调中校正。</summary>
    protected virtual void ApplyVisualState(float progress)
    {
        if (group != null)
            group.alpha = Mathf.Clamp01(progress) * targetOpenAlpha;
    }

    private float GetVisibility() => visibility;

    /// <summary>具名插值写入点，便于子类和 MOD 定位。</summary>
    private void SetVisibility(float value)
    {
        visibility = value;
        ApplyVisualState(value);
    }
    #endregion

    #region 配置与统一控制
    public void SetAnimationId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
            throw new ArgumentException("AnimationId 不能为空或包含首尾空格。", nameof(id));

        animationId = id;
        if (manager != null)
            RefreshConfiguration();
    }

    /// <summary>指定独立动画节点；运行中的过渡先完成，避免旧节点残留姿态。</summary>
    public void SetMotionRoot(RectTransform target)
    {
        if (target != null && target != transform && !target.IsChildOf(transform))
            throw new ArgumentException("动画节点必须属于同一个面板。", nameof(target));

        CompleteAnimation();
        RestoreRestPose();
        motionRoot = target;
    }

    /// <summary>缓存配置；进行中的完整行程继续使用自己的配置快照。</summary>
    public void RefreshConfiguration()
    {
        if (manager == null)
            return;

        configuredProfile = manager.GetProfile(animationId);
    }

    /// <summary>只改当前 UI 动画根 Tween 的倍率，不修改 DOTween.timeScale 或 Time.timeScale。</summary>
    public void ApplyPlaybackSpeed()
    {
        if (activeTween != null && activeTween.IsActive() && manager != null)
            activeTween.timeScale = manager.PlaybackSpeed;
    }

    /// <summary>立即完成当前视觉过渡并回调 BasePanel；重复调用安全。</summary>
    public void CompleteAnimation()
    {
        if (!IsTransitioning)
            return;

        FinishTransition(transitionRevision);
    }

    /// <summary>取消当前动画且不通知 BasePanel，仅供销毁/显式撤销使用。</summary>
    public void CancelAnimation()
    {
        transitionRevision++;
        completion = null;
        KillOwnedTween();
        RestoreRestPose();
        playingProfile = null;
    }

    private void FinishTransition(int revision)
    {
        if (revision != transitionRevision)
            return;

        KillOwnedTween();
        visibility = targetOpening ? 1f : 0f;
        ApplyVisualState(visibility);
        RestoreRestPose();
        playingProfile = null;

        Action callback = completion;
        completion = null;
        callback?.Invoke();
    }

    /// <summary>外部 Kill 也提交当前目标，防止面板永久停在半透明状态。</summary>
    private void HandleTweenKilled(int revision)
    {
        if (revision != transitionRevision || activeTween == null)
            return;

        activeTween = null;
        FinishTransition(revision);
    }

    /// <summary>先清空引用再 Kill，使自身替换/完成不会触发外部 Kill 兜底。</summary>
    private void KillOwnedTween()
    {
        Tween previous = activeTween;
        activeTween = null;
        if (previous != null && previous.IsActive())
            previous.Kill(false);
    }
    #endregion
}
