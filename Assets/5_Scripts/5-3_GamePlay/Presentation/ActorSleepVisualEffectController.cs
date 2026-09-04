using UnityEngine;

/// <summary>
/// 生物睡眠表现控制器：把 AI 的睡眠状态映射为绑定角色根节点的池化 Z 粒子，
/// 统一处理头部偏移、非叠加播放与角色禁用时的回收，避免各动物重复维护特效生命周期。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorSleepVisualEffectController : MonoBehaviour
{
    #region Config

    [SerializeField]
    [Tooltip("交给 VisualEffectManager 解析的睡眠粒子预制体名称。")]
    private string effectName = "SleepZZZEffect";

    [SerializeField]
    [Tooltip("粒子发射点相对生物根节点的位置。")]
    private Vector3 localOffset = new Vector3(0.28f, 0.58f, 0f);

    #endregion

    #region Runtime State

    private VisualEffectManager _effectManager;
    private Transform _actorRoot;

    #endregion

    #region Public API

    /// <summary>同步睡眠表现；进入时播放唯一实例，离开时立即回收。</summary>
    public void SetSleeping(Transform actorRoot, bool sleeping)
    {
        if (_actorRoot != null && _actorRoot != actorRoot)
            StopEffect();

        _actorRoot = actorRoot;
        if (!sleeping)
        {
            StopEffect();
            return;
        }

        _effectManager = VisualEffectManager.Instance;
        _effectManager.PlayEffect(
            _actorRoot,
            effectName,
            _actorRoot,
            localOffset,
            -1f,
            EffectStackMode.NonStackable);
    }

    #endregion

    #region Lifecycle

    /// <summary>角色休眠、回收或销毁时同步清掉仍绑定的粒子。</summary>
    private void OnDisable()
    {
        StopEffect();
    }

    #endregion

    #region Helpers

    /// <summary>回收当前角色的睡眠特效。</summary>
    private void StopEffect()
    {
        if (_effectManager != null && _actorRoot != null)
            _effectManager.StopOwnerEffect(_actorRoot, effectName);

        _effectManager = null;
        _actorRoot = null;
    }

    #endregion
}
