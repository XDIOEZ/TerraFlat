using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// 麦克风蓄力修饰模块：仅本地玩家持有武器并开始有效蓄力后录制麦克风，
/// 用最近 1024 帧采样的均方根音量计算 0～1 吹气强度，映射为 0%～100% 的额外速度与伤害。
/// 不保存录音；松开、取消、卸载或失焦时立即停止采集并释放 AudioClip。
/// </summary>
public sealed class Mod_MicrophoneCharge : Module, IProjectileChargeModifier, IItemModuleDependencyBinder
{
    public const string PersistedModuleId = "Mod_MicrophoneCharge";

    #region 配置

    [Range(0f, 1f), Tooltip("低于该均方根音量时不提供吹气加成。")]
    public float NoiseFloorRms = 0.015f;

    [Range(0f, 1f), Tooltip("达到该均方根音量时提供 100% 的额外速度与伤害。")]
    public float FullBonusRms = 0.15f;

    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => PersistedModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 运行时状态

    private const int SampleWindowFrames = 1024;
    private const int PreferredSampleRate = 44100;
    private const float VolumeResponseSeconds = 0.08f;

    private AudioClip _recordingClip;
    private float[] _sampleBuffer;
    private string _deviceName;
    private float _volume01;
    private int _lastSamplePosition = -1;
    private bool _hasCompleteWindow;
    private bool _charging;
    private bool _captureAttempted;
#if UNITY_ANDROID && !UNITY_EDITOR
    private bool _permissionRequested;
#endif
#if UNITY_IOS && !UNITY_EDITOR
    private AsyncOperation _permissionRequest;
#endif

    #endregion

    #region 生命周期

    /// <summary>确保模块数据拥有稳定 ID。</summary>
    public override void Awake()
    {
        Data ??= new Ex_ModData_MemoryPackable();
        Data.ID = PersistedModuleId;
        base.Awake();
    }

    /// <summary>吹气模块只能装配在具有唯一蓄力发射模块的物品上。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        modules.RequireSingleModById<Mod_Bow>(Mod_Bow.PersistedModuleId);
    }

    /// <summary>装载时校验音量区间并清除上一次使用的录音状态。</summary>
    public override void Load()
    {
        if (FullBonusRms <= NoiseFloorRms)
            throw new System.InvalidOperationException($"{name} 的满加成音量必须高于静音阈值。");
        CancelCharge();
    }

    /// <summary>麦克风采集状态不进入物品存档。</summary>
    public override void Save()
    {
    }

    /// <summary>物品卸载时结束录音。</summary>
    public override void Unload()
    {
        CancelCharge();
    }

    /// <summary>组件停用时结束录音。</summary>
    private void OnDisable()
    {
        CancelCharge();
    }

    #endregion

    #region 蓄力采样

    /// <summary>本地玩家开始蓄力时申请麦克风并清零本次音量。</summary>
    public void StartCharge()
    {
        CancelCharge();
        if (item?.Owner is not Player player || !player.IsLocalProfile)
            return;

        _charging = true;
        _captureAttempted = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        _permissionRequested = false;
#endif
        TryStartCapture();
    }

    /// <summary>蓄力期间持续读取最近一小段采样并平滑音量。</summary>
    public void UpdateCharge(float deltaTime)
    {
        if (!_charging)
            return;
        if (!Application.isFocused || item?.Owner is not Player player || !player.IsLocalProfile)
        {
            CancelCharge();
            return;
        }

        if (_recordingClip == null)
            TryStartCapture();
        ReadCurrentVolume(Mathf.Max(0f, deltaTime));
    }

    /// <summary>松手时快照当前吹气强度，并在返回倍率前停止录音。</summary>
    public ProjectileLaunchMultipliers CompleteCharge()
    {
        if (_charging)
            ReadCurrentVolume(Time.unscaledDeltaTime);
        float bonus01 = _charging ? Mathf.Clamp01(_volume01) : 0f;
        CancelCharge();
        return new ProjectileLaunchMultipliers(1f + bonus01, 1f + bonus01);
    }

    /// <summary>取消蓄力后清零音量并释放麦克风。</summary>
    public void CancelCharge()
    {
        _charging = false;
        _volume01 = 0f;
        StopCapture();
    }

    /// <summary>仅在本地录音权限允许时打开一个循环采样缓冲。</summary>
    private void TryStartCapture()
    {
        if (!_charging || _captureAttempted || _recordingClip != null)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            if (!_permissionRequested)
            {
                Permission.RequestUserPermission(Permission.Microphone);
                _permissionRequested = true;
            }
            return;
        }
#endif
#if UNITY_IOS && !UNITY_EDITOR
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            _permissionRequest ??= Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if (!_permissionRequest.isDone)
                return;
            _captureAttempted = true;
            return;
        }
#endif

        _captureAttempted = true;
        string[] devices = Microphone.devices;
        if (devices.Length == 0)
            return;

        _deviceName = devices[0];
        Microphone.GetDeviceCaps(_deviceName, out int minRate, out int maxRate);
        int sampleRate = maxRate > 0
            ? Mathf.Clamp(PreferredSampleRate, minRate, maxRate)
            : PreferredSampleRate;
        _recordingClip = Microphone.Start(_deviceName, true, 1, sampleRate);
        if (_recordingClip == null)
        {
            _deviceName = null;
            return;
        }

        _sampleBuffer = new float[SampleWindowFrames * _recordingClip.channels];
        _lastSamplePosition = -1;
        _hasCompleteWindow = false;
    }

    /// <summary>读取环形录音缓冲的最新窗口，并把音量映射到 0～1。</summary>
    private void ReadCurrentVolume(float deltaTime)
    {
        if (_recordingClip == null)
            return;
        if (_hasCompleteWindow && !Microphone.IsRecording(_deviceName))
        {
            CancelCharge();
            return;
        }

        int position = Microphone.GetPosition(_deviceName);
        if (position <= 0 || position == _lastSamplePosition)
            return;
        if (!_hasCompleteWindow && position < SampleWindowFrames)
            return;

        int offset = position - SampleWindowFrames;
        if (offset < 0)
            offset += _recordingClip.samples;
        if (!_recordingClip.GetData(_sampleBuffer, offset))
        {
            CancelCharge();
            return;
        }

        _lastSamplePosition = position;
        _hasCompleteWindow = true;
        float sumSquares = 0f;
        for (int i = 0; i < _sampleBuffer.Length; i++)
            sumSquares += _sampleBuffer[i] * _sampleBuffer[i];
        float rms = Mathf.Sqrt(sumSquares / _sampleBuffer.Length);
        float target01 = Mathf.InverseLerp(NoiseFloorRms, FullBonusRms, rms);
        float response = 1f - Mathf.Exp(-deltaTime / VolumeResponseSeconds);
        _volume01 = Mathf.Lerp(_volume01, target01, response);
    }

    /// <summary>停止此模块持有的录音并释放采样缓冲。</summary>
    private void StopCapture()
    {
        if (_recordingClip != null)
        {
            Microphone.End(_deviceName);
            Destroy(_recordingClip);
        }
        _recordingClip = null;
        _sampleBuffer = null;
        _deviceName = null;
        _lastSamplePosition = -1;
        _hasCompleteWindow = false;
    }

    #endregion
}
