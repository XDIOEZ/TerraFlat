using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 管理观察区域风力与 GPU 全局参数，设施消耗仍读取自身位置的风。
/// </summary>
public partial class WeatherMgr
{
    #region 全区域风推动过渡

    private readonly RegionalWindPushField regionalWindPush = new();
    private int windPushFieldFrame = -1;
    private ulong windPushFieldRevision = ulong.MaxValue;

    private void MaintainWindPushField()
    {
        if (!_weatherRuntimeAllowed || !isActiveAndEnabled || regionalEngine == null)
        {
            ResetWindPushField();
            return;
        }
        if (windPushFieldFrame == Time.frameCount && windPushFieldRevision == regionalEngine.WindQueryRevision)
            return;
        // 秒轴不随游戏日历跳时，同帧新天气用版本号补同步，所有物体共用同一批结果。
        regionalWindPush.Synchronize(regionalEngine, Time.timeAsDouble, _weatherTransitionSeconds);
        windPushFieldFrame = Time.frameCount;
        windPushFieldRevision = regionalEngine.WindQueryRevision;
    }

    /// <summary>按物体自身起吹档位采样当地推动，不借本机镜头或风声表现强度。</summary>
    public Vector2 SampleWindPushVelocityAt(Vector2 position, int requiredWindLevel)
    {
        MaintainWindPushField();
        return regionalWindPush.Sample(new Unity.Mathematics.double2(position.x, position.y), requiredWindLevel);
    }

    private void ResetWindPushField()
    {
        regionalWindPush.Reset();
        windPushFieldFrame = -1;
        windPushFieldRevision = ulong.MaxValue;
    }

    #endregion

    #region 区域风与风浪表现

    private static readonly int GlobalWindStrengthShaderId = Shader.PropertyToID("_GlobalWindStrength");
    private static readonly int GlobalWindDirectionShaderId = Shader.PropertyToID("_GlobalWindDirection");
    private static readonly int OceanWaveFactorsShaderId = Shader.PropertyToID("_OceanWaveFactors");
    private static readonly int OceanWaveTimeShaderId = Shader.PropertyToID("_OceanWaveTime");
    private float oceanWaveTime; // 独立积分风浪时钟，风速变化不跳相位，也不改潮汐时钟
    private Vector2 presentedWindDirection;

    // 表现层共用已平滑的本机风，后台世界和禁用天气的维度不输出风效。
    public Vector2 PresentedWind => isActiveAndEnabled && presentationActive && IsLocalWeatherWorld &&
        _weatherRuntimeAllowed ? presentedWindDirection * _presentedWindStrength : Vector2.zero;

    /// <summary>只推进视觉风浪；暂停时停止，普通客户端读取已复制风力。</summary>
    private void AdvanceOceanWaveClock(float deltaTime)
    {
        if (!_weatherRuntimeAllowed)
            return;
        oceanWaveTime += Mathf.Max(0f, deltaTime) *
            WaterEnvironmentRules.ResolveOceanWaveFactors(_presentedWindStrength).x;
        Shader.SetGlobalFloat(OceanWaveTimeShaderId, oceanWaveTime);
    }

    [ShowInInspector, ReadOnly, LabelText("观察区域风力")]
    public float CurrentWindStrength => GetCurrentWindStrength();

    /// <summary>读取当前维度可见的风力；禁用环境反馈的维度返回零。</summary>
    public float GetCurrentWindStrength()
    {
        if (!_weatherRuntimeAllowed)
            return 0f;

        return TryGetLocalWeather(out var sample) ? sample.WindStrength : 0f;
    }

    /// <summary>由状态权威限时覆盖观察区域风力。</summary>
    public void SetWindStrength(float strength)
    {
        if (surfaceWork.IsAdvanceBlocked)
        {
            Debug.LogWarning("[WeatherMgr] 正在冻结存档天气，修改风力请稍后重试。");
            return;
        }
        if (!GameNetwork.HasStateAuthority)
        {
            if (EnableDebugLog)
                Debug.LogWarning("[WeatherMgr] 普通客户端不能修改权威风力状态。");
            return;
        }

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return;

        if (!PrepareRegionalContext() || !TryGetCurrentTimeData(out var clock)) return;
        AdvanceRegionalWeather();
        Vector2 position = ObservationPosition();
        FlatWorld.WorldModel.Int2 region = regionalEngine.ResolveRegion(new FlatWorld.WorldModel.Int2(
            Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        double now = GetRegionalStepTime(clock);
        regionalEngine.Advance(region, now);
        regionalEngine.ForceElements(region, now, GetCurrentDayLength() * 0.15d, windStrength: Mathf.Clamp01(strength));
        PersistRegionalStates();
        UpdateLocalWeatherProjection();
        if (regionalEngine.TryGetRegionSnapshot(region, out var sample))
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(ownedWorld, sample));
        PublishAuthoritativeWeatherState();
    }

    // 风向按最短夹角过渡，反向风不会经过零向量，静风保留最后方向直到退出世界。
    private bool MaintainWindDirection(Vector2 wind, float transitionStep)
    {
        if (wind.sqrMagnitude <= 0.000001f)
            return false;

        Vector2 previous = presentedWindDirection;
        Vector2 target = wind.normalized;
        if (previous.sqrMagnitude <= 0.000001f)
            presentedWindDirection = target;
        else
        {
            float currentAngle = Mathf.Atan2(previous.y, previous.x) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
            float angle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, 180f * transitionStep) * Mathf.Deg2Rad;
            presentedWindDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        return presentedWindDirection.x != previous.x || presentedWindDirection.y != previous.y;
    }

    /// <summary>把过渡后的观察区域风力和吹去方向写入 Shader 全局参数。</summary>
    private void RefreshWindFeedback()
    {
        if (!IsLocalWeatherWorld || !presentationActive) return;
        snowController?.SetWind(presentedWindDirection * _presentedWindStrength);
        Shader.SetGlobalFloat(GlobalWindStrengthShaderId, _presentedWindStrength);
        Shader.SetGlobalVector(GlobalWindDirectionShaderId,
            new Vector4(presentedWindDirection.x, presentedWindDirection.y, 0f, 0f));
        Vector3 factors = WaterEnvironmentRules.ResolveOceanWaveFactors(_presentedWindStrength);
        Shader.SetGlobalVector(OceanWaveFactorsShaderId, new Vector4(factors.x, factors.y, factors.z, 0f));
    }

    /// <summary>离开世界或进入禁用天气的维度时清除残留风力表现。</summary>
    private void DeactivateWindFeedback()
    {
        oceanWaveTime = 0f;
        presentedWindDirection = Vector2.zero;
        Shader.SetGlobalFloat(GlobalWindStrengthShaderId, 0f);
        Shader.SetGlobalVector(GlobalWindDirectionShaderId, Vector4.zero);
        Vector3 factors = WaterEnvironmentRules.ResolveOceanWaveFactors(0f);
        Shader.SetGlobalVector(OceanWaveFactorsShaderId, new Vector4(factors.x, factors.y, factors.z, 0f));
        Shader.SetGlobalFloat(OceanWaveTimeShaderId, 0f);
    }

    #endregion
}
