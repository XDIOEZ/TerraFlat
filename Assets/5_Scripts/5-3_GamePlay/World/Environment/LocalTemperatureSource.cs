using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 供篝火、加热器和空调复用的局部温度源组件。默认半径 6 格、中心升温 15℃，负值可制冷。
/// 只负责向环境场注册影响；燃料、电力、目标温度控制和保存由设备模块负责，并通过 Configure 驱动。
/// 每 0.25 秒发布位置快照；静止源不会重建缓存，停用或回池立即撤销影响。
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalTemperatureSource : MonoBehaviour
{
    #region 参数

    [SerializeField, Range(0.5f, 64f), Tooltip("影响半径，单位为地块。")]
    private float radius = 6f;
    [SerializeField, Tooltip("源中心的摄氏度增量；正数加热，负数制冷。")]
    private float celsiusOffset = 15f;
    private float nextPublishTime;
    private TemperatureMgr registeredManager;

    #endregion

    #region 生命周期

    private void OnEnable() => nextPublishTime = 0f;

    private void Update()
    {
        if (Time.time < nextPublishTime)
            return;
        nextPublishTime = Time.time + 0.25f;
        Publish();
    }

    private void OnDisable()
    {
        if (registeredManager != null)
            registeredManager.RemoveLocalTemperatureSource(this);
        registeredManager = null;
    }

    #endregion

    #region 设备入口

    /// <summary>设备根据燃烧、供电或恒温控制更新有效强度，零强度等同于关闭温度影响。</summary>
    public void Configure(float influenceRadius, float temperatureOffset)
    {
        radius = influenceRadius;
        celsiusOffset = temperatureOffset;
        if (isActiveAndEnabled)
            Publish();
    }

    private void Publish()
    {
        // 只发布当前世界场景的设备，预加载或尚未卸载的旧维度设备不能影响新世界。
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld ||
            gameObject.scene != SceneManager.GetActiveScene())
        {
            if (registeredManager != null)
                registeredManager.RemoveLocalTemperatureSource(this);
            return;
        }
        registeredManager = TemperatureMgr.Instance;
        if (registeredManager != null)
            registeredManager.SetLocalTemperatureSource(this, transform.position, radius, celsiusOffset);
    }

    #endregion
}
