using UnityEngine;

/// <summary>
/// 生物睡眠表现控制器：把 AI 的睡眠状态映射为独立特效节点下跟随角色的池化 Z 粒子。
/// 默认发射偏移为角色局部坐标 (0.28, 0.58, 0)，每个角色只播放一个实例；
/// 区块休眠时回收粒子并保留表现请求，重新激活时恢复，避免在角色停用回调中搬移其子节点。
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
    /// <summary>AI 最近一次提交的睡眠表现请求，跨区块休眠保留。</summary>
    private bool _sleeping;

    #endregion

    #region Public API

    /// <summary>同步睡眠表现；进入时播放唯一实例，离开时立即回收。</summary>
    public void SetSleeping(Transform actorRoot, bool sleeping)
    {
        if (_actorRoot != null && _actorRoot != actorRoot)
            StopEffect();

        _actorRoot = actorRoot;
        _sleeping = sleeping;
        if (!_sleeping || !isActiveAndEnabled)
        {
            StopEffect();
            return;
        }

        PlayEffect();
    }

    #endregion

    #region Lifecycle

    /// <summary>区块恢复显示时重建仍处于睡眠状态的粒子。</summary>
    private void OnEnable()
    {
        if (_sleeping && _actorRoot != null)
            PlayEffect();
    }

    /// <summary>独立粒子在角色移动结束后同步发射点，不依赖角色父子层级。</summary>
    private void LateUpdate()
    {
        if (_effectManager == null || _actorRoot == null)
            return;

        GameObject effectObject = _effectManager.GetOwnerEffect(_actorRoot, effectName);
        if (effectObject != null)
            SynchronizeTransform(effectObject.transform);
    }

    /// <summary>角色休眠、回收或销毁时同步清掉仍绑定的粒子。</summary>
    private void OnDisable()
    {
        StopEffect();
    }

    #endregion

    #region Helpers

    /// <summary>Owner 仅用于登记；粒子由特效管理器持有，避免随角色的激活过程修改层级。</summary>
    private void PlayEffect()
    {
        _effectManager = VisualEffectManager.Instance;
        Transform effectRoot = _effectManager.transform;
        Vector3 position = _actorRoot.TransformPoint(localOffset);
        GameObject effectObject = _effectManager.PlayEffect(
            _actorRoot,
            effectName,
            effectRoot,
            effectRoot.InverseTransformPoint(position),
            -1f,
            EffectStackMode.NonStackable);
        if (effectObject != null)
            SynchronizeTransform(effectObject.transform);
    }

    /// <summary>同步角色的头部偏移、旋转和缩放；独立特效根节点使用单位缩放。</summary>
    private void SynchronizeTransform(Transform effectTransform)
    {
        effectTransform.SetPositionAndRotation(_actorRoot.TransformPoint(localOffset), _actorRoot.rotation);
        effectTransform.localScale = _actorRoot.lossyScale;
    }

    /// <summary>回收当前角色的睡眠特效。</summary>
    private void StopEffect()
    {
        if (_effectManager != null && _actorRoot != null)
            _effectManager.StopOwnerEffect(_actorRoot, effectName);

        _effectManager = null;
    }

    #endregion
}
