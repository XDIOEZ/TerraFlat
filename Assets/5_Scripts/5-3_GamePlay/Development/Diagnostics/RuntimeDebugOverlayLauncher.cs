using UnityEngine;

/// <summary>
/// 在 GameRes 之前创建独立调试悬浮窗；Prefab 使用直接引用，启动失败时不依赖 Addressables 或 UIManager。
/// </summary>
[DefaultExecutionOrder(-31900)]
[DisallowMultipleComponent]
public sealed class RuntimeDebugOverlayLauncher : MonoBehaviour
{
    #region 配置

    /// <summary>最早启动阶段直接实例化的调试悬浮窗 Prefab。</summary>
    [SerializeField] private GameObject overlayPrefab;

    #endregion

    #region 生命周期

    /// <summary>在资源管理器启动前创建跨场景唯一悬浮窗。</summary>
    private void Awake()
    {
        if (RuntimeDebugOverlay.HasInstance)
            return;

        if (overlayPrefab == null)
        {
            Debug.LogError("[RuntimeDebugOverlay] WorldManager 未绑定调试悬浮窗 Prefab。");
            return;
        }

        Instantiate(overlayPrefab);
    }

    #endregion
}
