using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 相机跟随式雨效控制器。
/// 雨层固定在正交相机上缘，并按雨滴初速度、相机高度和 1.12 倍安全余量动态计算生命周期，
/// 使雨滴刚好持续到画面下缘外侧，避免寿命过长散落到地图外或寿命过短导致下半屏断雨。
/// </summary>
public class RainEffectController : MonoBehaviour
{
#region 字段

    [SerializeField] private bool _followMainCamera = true; // 是否跟随主摄像机
    [SerializeField] private Vector3 _positionOffset = Vector3.zero; // 相机跟随基础偏移
    [SerializeField] private bool _adaptOffsetByCameraSize = true; // 是否根据相机尺寸动态修正偏移
    [SerializeField] private float _offsetHeightFactor = 1f; // 相机高度偏移系数（正交相机按orthographicSize）
    [SerializeField] private bool _lockRotation = true; // 是否锁定旋转
    [SerializeField] private bool _syncScaleByOrthographicSize = true; // 是否按正交尺寸同步缩放
    [SerializeField] private float _referenceOrthographicSize = 5f; // 正交相机参考尺寸
    [SerializeField] private float _coveragePadding = 1.1f; // 覆盖冗余倍率，避免缩放露边
    [SerializeField] private bool _fitLifetimeToCamera = true; // 是否根据相机高度补足雨滴生命周期
    [SerializeField, Min(0.01f)] private float _minimumParticleLifetime = 0.35f; // 生命周期下限
    [SerializeField, Min(1f)] private float _lifetimeCoverageMultiplier = 1.12f; // 下边缘安全覆盖倍率
    [SerializeField] private ParticleSystem[] _targetParticleSystems; // 受控粒子系统列表（为空则自动查找子节点）
    [SerializeField] private float _minEmissionFactor = 0.05f; // 最小发射倍率（0~1）
    [SerializeField] private float _minMaxParticlesFactor = 0.1f; // 最小粒子上限倍率（0~1）

    private float _currentIntensity = 1f; // 当前雨强度（仅用于外部同步）
    private ParticleSystem[] _runtimeParticleSystems; // 运行时粒子系统缓存
    private float[] _baseRateOverTimeMultipliers; // 初始每秒发射倍率
    private float[] _baseRateOverDistanceMultipliers; // 初始按距离发射倍率
    private int[] _baseMaxParticles; // 初始最大粒子数
    private float[] _lastAppliedLifetimes; // 最近写入的生命周期，避免每帧重复设置

#endregion

#region 生命周期

    private void Awake()
    {
        EnsureParticleSystemCache();
        ApplyParticleByIntensity();
        SyncToMainCamera();
    }

    private void Reset()
    {
        _positionOffset = Vector3.zero;
    }

    private void OnValidate()
    {
        _referenceOrthographicSize = Mathf.Max(0.01f, _referenceOrthographicSize);
        _coveragePadding = Mathf.Max(1f, _coveragePadding);
        _minimumParticleLifetime = Mathf.Max(0.01f, _minimumParticleLifetime);
        _lifetimeCoverageMultiplier = Mathf.Max(1f, _lifetimeCoverageMultiplier);
        _minEmissionFactor = Mathf.Clamp01(_minEmissionFactor);
        _minMaxParticlesFactor = Mathf.Clamp01(_minMaxParticlesFactor);
    }

    private void LateUpdate()
    {
        SyncToMainCamera();
    }

#endregion

#region 公共方法

    public void ApplySettings(float intensity)
    {
        _currentIntensity = Mathf.Clamp01(intensity);
        ApplyParticleByIntensity();
        SyncToMainCamera();
    }

    public void SetEmissionArea(Vector2 areaSize)
    {
        // 已废弃：粒子发射区域由粒子系统自身配置。
    }

    public void SyncToCamera(Camera targetCamera, float intensity)
    {
        _currentIntensity = Mathf.Clamp01(intensity);
        ApplyParticleByIntensity();
        SyncTransformToCamera(targetCamera);
    }

#endregion

#region 私有方法

    private void SyncToMainCamera()
    {
        if (!_followMainCamera)
        {
            return;
        }

        SyncTransformToCamera(Camera.main);
    }

    private void SyncTransformToCamera(Camera targetCamera)
    {
        if (targetCamera == null)
        {
            return;
        }

        Vector3 runtimeOffset = GetRuntimeOffset(targetCamera);
        transform.position = targetCamera.transform.position + runtimeOffset;

        if (_lockRotation)
        {
            transform.rotation = Quaternion.identity;
        }
        else
        {
            transform.rotation = targetCamera.transform.rotation;
        }

        SyncParticleLifetimeToCamera(targetCamera, runtimeOffset);

        if (!_syncScaleByOrthographicSize || !targetCamera.orthographic)
        {
            return;
        }

        // 根据相机正交尺寸和宽高比计算缩放，确保完全覆盖整个屏幕
        float orthographicHeight = targetCamera.orthographicSize * 2f * _coveragePadding; // 加冗余后的正交高度
        float aspectRatio = targetCamera.aspect; // 相机宽高比

        // 相对于参考尺寸的缩放
        float referenceHeight = _referenceOrthographicSize * 2f;
        float yScale = Mathf.Max(0.25f, orthographicHeight / referenceHeight);
        float xScale = yScale * aspectRatio; // X轴乘以宽高比以覆盖整个宽度

        transform.localScale = new Vector3(xScale, yScale, 1f);
    }

    private Vector3 GetRuntimeOffset(Camera targetCamera)
    {
        Vector3 runtimeOffset = _positionOffset;
        runtimeOffset.z = 1f;

        if (!_adaptOffsetByCameraSize || !targetCamera.orthographic)
        {
            return runtimeOffset;
        }

        float adaptiveYOffset = targetCamera.orthographicSize * _offsetHeightFactor;
        runtimeOffset.y += adaptiveYOffset;
        return runtimeOffset;
    }

    /// <summary>按从发射边缘到相机下缘的实际距离调整生命周期。</summary>
    private void SyncParticleLifetimeToCamera(Camera targetCamera, Vector3 runtimeOffset)
    {
        if (!_fitLifetimeToCamera || !targetCamera.orthographic)
        {
            return;
        }

        EnsureParticleSystemCache();
        if (_runtimeParticleSystems == null || _runtimeParticleSystems.Length == 0)
        {
            return;
        }

        // 发射边缘位于相机上方，目标距离为发射高度到下边缘，并额外越过少量边界防止露缝。
        float fallDistance = Mathf.Max(0.01f, runtimeOffset.y + targetCamera.orthographicSize);
        for (int i = 0; i < _runtimeParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = _runtimeParticleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particleSystem.main;
            float fallSpeed = GetMaximumStartSpeed(main.startSpeed);
            if (fallSpeed <= 0.01f)
            {
                continue;
            }

            float targetLifetime = Mathf.Max(
                _minimumParticleLifetime,
                fallDistance / fallSpeed * _lifetimeCoverageMultiplier);
            if (_lastAppliedLifetimes != null && i < _lastAppliedLifetimes.Length &&
                Mathf.Approximately(_lastAppliedLifetimes[i], targetLifetime))
            {
                continue;
            }

            main.startLifetime = targetLifetime;
            if (_lastAppliedLifetimes != null && i < _lastAppliedLifetimes.Length)
            {
                _lastAppliedLifetimes[i] = targetLifetime;
            }
        }
    }

    /// <summary>取得当前粒子系统的最大初始下落速度。</summary>
    private static float GetMaximumStartSpeed(ParticleSystem.MinMaxCurve startSpeed)
    {
        return Mathf.Max(
            Mathf.Abs(startSpeed.constantMin),
            Mathf.Abs(startSpeed.constantMax),
            Mathf.Abs(startSpeed.curveMultiplier));
    }

    private void EnsureParticleSystemCache()
    {
        if (_runtimeParticleSystems != null && _runtimeParticleSystems.Length > 0)
        {
            return;
        }

        if (_targetParticleSystems != null && _targetParticleSystems.Length > 0)
        {
            List<ParticleSystem> validParticles = new List<ParticleSystem>();
            for (int i = 0; i < _targetParticleSystems.Length; i++)
            {
                if (_targetParticleSystems[i] != null)
                {
                    validParticles.Add(_targetParticleSystems[i]);
                }
            }

            _runtimeParticleSystems = validParticles.ToArray();
        }
        else
        {
            _runtimeParticleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        if (_runtimeParticleSystems == null || _runtimeParticleSystems.Length == 0)
        {
            Debug.LogError($"[RainEffectController] 未找到可控制的粒子系统，物体={name}");
            _runtimeParticleSystems = new ParticleSystem[0];
            _baseRateOverTimeMultipliers = new float[0];
            _baseRateOverDistanceMultipliers = new float[0];
            _baseMaxParticles = new int[0];
            _lastAppliedLifetimes = new float[0];
            return;
        }

        int count = _runtimeParticleSystems.Length;
        _baseRateOverTimeMultipliers = new float[count];
        _baseRateOverDistanceMultipliers = new float[count];
        _baseMaxParticles = new int[count];
        _lastAppliedLifetimes = new float[count];

        for (int i = 0; i < count; i++)
        {
            ParticleSystem particleSystem = _runtimeParticleSystems[i];
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            ParticleSystem.MainModule main = particleSystem.main;
            _baseRateOverTimeMultipliers[i] = emission.rateOverTimeMultiplier;
            _baseRateOverDistanceMultipliers[i] = emission.rateOverDistanceMultiplier;
            _baseMaxParticles[i] = Mathf.Max(1, main.maxParticles);
            _lastAppliedLifetimes[i] = -1f;
        }
    }

    private void ApplyParticleByIntensity()
    {
        EnsureParticleSystemCache();
        if (_runtimeParticleSystems == null || _runtimeParticleSystems.Length == 0)
        {
            return;
        }

        float emissionFactor = Mathf.Lerp(_minEmissionFactor, 1f, _currentIntensity);
        float maxParticlesFactor = Mathf.Lerp(_minMaxParticlesFactor, 1f, _currentIntensity);

        for (int i = 0; i < _runtimeParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = _runtimeParticleSystems[i];
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            ParticleSystem.MainModule main = particleSystem.main;

            emission.rateOverTimeMultiplier = _baseRateOverTimeMultipliers[i] * emissionFactor;
            emission.rateOverDistanceMultiplier = _baseRateOverDistanceMultipliers[i] * emissionFactor;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(_baseMaxParticles[i] * maxParticlesFactor));
        }
    }

#endregion
}
