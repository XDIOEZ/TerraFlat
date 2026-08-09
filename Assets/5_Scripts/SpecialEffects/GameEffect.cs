
using System;
using UnityEngine;

/// <summary>
/// 通用一次性特效基类。
/// 既支持旧的 Instantiate + Destroy 调用，也支持 VisualEffectManager 注入回收回调；
/// 特效自身只需要在结束时调用 ReturnToPoolOrDestroy，即可自动适配对象池。
/// </summary>
public abstract class GameEffect : MonoBehaviour
{
    #region Pool Lifecycle

    private Action returnToPool;

    /// <summary>由对象池在取出实例时调用，子类可在这里清理上一次播放状态。</summary>
    public virtual void OnSpawnedFromPool()
    {
    }

    /// <summary>由对象池在回收实例时调用，子类可在这里停止协程并恢复默认显示。</summary>
    public virtual void OnReturnedToPool()
    {
    }

    /// <summary>注入对象池回调；留空时保持旧逻辑，结束后销毁自身。</summary>
    public void SetPoolReturnCallback(Action callback)
    {
        returnToPool = callback;
    }

    /// <summary>优先回收到对象池，没有池化回调时才销毁对象。</summary>
    protected void ReturnToPoolOrDestroy()
    {
        Action callback = returnToPool;
        if (callback != null)
        {
            callback.Invoke();
            return;
        }

        Destroy(gameObject);
    }

    #endregion

    #region Effect API

    /// <summary>开始播放特效。</summary>
    public abstract void Effect(Transform Sender, object Data = null);

    #endregion
}
