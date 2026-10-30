using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 供篝火、加热器和空调复用的局部温度源组件。默认半径 6 格、目标环境温度 40℃。
/// 只负责向环境场注册影响；燃料、电力、目标温度控制和保存由设备模块负责，并通过 Configure 驱动。
/// 每 0.25 秒发布位置快照；静止源不会重建缓存，停用或回池立即撤销影响。
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalTemperatureSource : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    #region 参数

    [SerializeField, Range(0.5f, 64f), Tooltip("影响半径，单位为地块。")]
    private float radius = 6f;
    [SerializeField, Tooltip("源中心期望的环境温度；高于当前环境时加热，低于时制冷。")]
    private float targetTemperature = 40f;
    private bool isEmitting = true;
    private bool cooling;
    private double nextPublishTime;
    private TemperatureMgr registeredManager;
    public float Radius => radius;
    public float TargetTemperature => targetTemperature;

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        nextPublishTime = 0d;
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.WorldSimulation);
    }

    public void SimulateFixedStep(float deltaTime)
    {
        if (GameplayFixedStepSimulation.CurrentTime < nextPublishTime)
            return;
        nextPublishTime = GameplayFixedStepSimulation.CurrentTime + 0.25d;
        Publish();
    }

    private void OnDisable()
    {
        GameplayFixedStepSimulation.Unregister(this);
        if (registeredManager != null)
            registeredManager.RemoveLocalTemperatureSource(this);
        registeredManager = null;
    }

    #endregion

    #region 设备入口

    /// <summary>设备根据燃烧、供电或恒温控制更新有效强度，零强度等同于关闭温度影响。</summary>
    public void Configure(float influenceRadius, float temperature, bool enabled, bool isCooling = false)
    {
        radius = influenceRadius;
        targetTemperature = temperature;
        isEmitting = enabled;
        cooling = isCooling;
        if (isActiveAndEnabled)
            Publish();
    }

    private void Publish()
    {
        if (!isEmitting)
        {
            if (registeredManager != null)
                registeredManager.RemoveLocalTemperatureSource(this);
            registeredManager = null;
            return;
        }

        // 热源向自身所属世界发布，主机切换画面后后台设备仍只加热本世界。
        bool registeredWorld = GameplayWorldRegistry.TryGet(gameObject.scene.name, out _);
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld ||
            !registeredWorld && gameObject.scene != GameplayWorldRegistry.CurrentScene)
        {
            if (registeredManager != null)
                registeredManager.RemoveLocalTemperatureSource(this);
            return;
        }
        using var worldScope = registeredWorld ? GameplayWorldRegistry.UseWorld(gameObject.scene.name) : null;
        registeredManager = TemperatureMgr.Instance;
        if (registeredManager != null)
            registeredManager.SetLocalTemperatureSource(this, transform.position, radius, targetTemperature, cooling);
    }

    #endregion
}
