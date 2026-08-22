using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.Audio;

namespace FlatWorld.Audio
{
    /// <summary>
    /// 跨场景音频服务：事件解析、声源池、并发限制、优先级抢占、淡入淡出和用户音量。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour, ISettingsProvider
    {
        private const string RuntimeConfigResourcePath = "Audio/AudioRuntimeConfig";
        private const string CatalogResourcePath = "Audio/AudioCatalog";
        private const string SettingsKeyPrefix = "FlatWorld.Audio.v1.";

        public const string SettingsProviderId = "audio";
        public const string MasterVolumeSettingKey = "audio.masterVolume";
        public const string MusicVolumeSettingKey = "audio.musicVolume";
        public const string SfxVolumeSettingKey = "audio.sfxVolume";
        public const string UiVolumeSettingKey = "audio.uiVolume";
        public const string AmbientVolumeSettingKey = "audio.ambientVolume";
        public const string VoiceVolumeSettingKey = "audio.voiceVolume";
        public const string MutedSettingKey = "audio.muted";

        private static AudioService instance;

        [SerializeField] private AudioRuntimeConfig runtimeConfig;
        [SerializeField] private AudioCatalog catalog;
        [SerializeField] private bool verboseMissingCueWarnings = true;

        private readonly List<Voice> activeVoices = new List<Voice>(32);
        private readonly Stack<Voice> availableVoices = new Stack<Voice>(16);
        private readonly Dictionary<string, float> lastPlayTime =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> warnedMissingCueIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AudioUserSettings userSettings;
        private readonly List<ISettingsSlider> settingSliders =
            new List<ISettingsSlider>(6);
        private readonly List<ISettingsToggle> settingToggles =
            new List<ISettingsToggle>(1);
        private int maxVoices = 48;
        private int totalVoices;
        private int nextVoiceId = 1;

        public static AudioService Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                instance = FindObjectOfType<AudioService>();
                if (instance != null)
                    return instance;

                GameObject root = new GameObject("[AudioService]");
                instance = root.AddComponent<AudioService>();
                return instance;
            }
        }

        public static bool HasInstance => instance != null;
        public AudioUserSettings UserSettings => userSettings;
        public AudioCatalog Catalog => catalog;
        public int ActiveVoiceCount => activeVoices.Count;
        public int MaxVoices => maxVoices;

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "音频";
        public int Order => 10;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => settingToggles;
        public IReadOnlyList<ISettingsSlider> SliderSettings => settingSliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

        public event Action<AudioUserSettings> SettingsChanged;

        private sealed class Voice
        {
            public GameObject GameObject;
            public AudioSource Source;
            public AudioCue Cue;
            public Transform FollowTarget;
            public int Id;
            public float StartedAt;
            public float BaseGain;
            public float Fade;
            public float FadeSpeed;
            public bool Stopping;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            LoadConfiguration();
            LoadUserSettings();
            InitializeSettingsProvider();
            SettingsProviderRegistry.Register(this);
            Prewarm(runtimeConfig != null ? runtimeConfig.InitialVoiceCount : 12);
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = activeVoices.Count - 1; i >= 0; i--)
            {
                Voice voice = activeVoices[i];
                if (voice.FollowTarget != null)
                    voice.GameObject.transform.position = voice.FollowTarget.position;

                if (voice.FadeSpeed > 0f)
                {
                    float target = voice.Stopping ? 0f : 1f;
                    voice.Fade = Mathf.MoveTowards(voice.Fade, target, voice.FadeSpeed * deltaTime);
                    ApplyVolume(voice);

                    if (voice.Stopping && voice.Fade <= 0f)
                    {
                        ReleaseVoice(voice);
                        continue;
                    }

                    if (!voice.Stopping && voice.Fade >= 1f)
                        voice.FadeSpeed = 0f;
                }

                if (!voice.Source.loop && !voice.Source.isPlaying)
                    ReleaseVoice(voice);
            }
        }

        private void OnDestroy()
        {
            SettingsProviderRegistry.Unregister(this);
            if (instance == this)
                instance = null;
        }

        #region 设置提供者

        /// <summary>把音频总线和静音状态以通用设置契约暴露给 UI。</summary>
        private void InitializeSettingsProvider()
        {
            settingSliders.Clear();
            settingToggles.Clear();

            settingSliders.Add(new SettingsSlider(
                new SettingDescriptor(
                    MasterVolumeSettingKey,
                    "主音量",
                    SettingControlType.Slider,
                    "audio",
                    order: 0),
                0f,
                1f,
                0.01f,
                () => userSettings != null ? userSettings.Master : 1f,
                value => SetMasterVolume(value)));
            settingSliders.Add(CreateBusVolumeSetting(
                MusicVolumeSettingKey,
                "音乐音量",
                AudioBus.Music,
                1));
            settingSliders.Add(CreateBusVolumeSetting(
                SfxVolumeSettingKey,
                "音效音量",
                AudioBus.Sfx,
                2));
            settingSliders.Add(CreateBusVolumeSetting(
                UiVolumeSettingKey,
                "界面音量",
                AudioBus.UI,
                3));
            settingSliders.Add(CreateBusVolumeSetting(
                AmbientVolumeSettingKey,
                "环境音量",
                AudioBus.Ambient,
                4));
            settingSliders.Add(CreateBusVolumeSetting(
                VoiceVolumeSettingKey,
                "语音音量",
                AudioBus.Voice,
                5));

            settingToggles.Add(new SettingsToggle(
                new SettingDescriptor(
                    MutedSettingKey,
                    "静音",
                    SettingControlType.Toggle,
                    "audio",
                    order: 0),
                () => userSettings != null && userSettings.Muted,
                value => SetMuted(value)));
        }

        private ISettingsSlider CreateBusVolumeSetting(
            string key,
            string displayName,
            AudioBus bus,
            int order)
        {
            return new SettingsSlider(
                new SettingDescriptor(
                    key,
                    displayName,
                    SettingControlType.Slider,
                    "audio",
                    order: order),
                0f,
                1f,
                0.01f,
                () => userSettings != null ? userSettings.GetBusVolume(bus) : 1f,
                value => SetBusVolume(bus, value));
        }

        public void ResetToDefaults() => ResetUserSettings();

        #endregion

        public void SetCatalog(AudioCatalog value)
        {
            catalog = value;
            catalog?.RebuildIndex();
            warnedMissingCueIds.Clear();
        }

        public bool TryGetCue(string cueId, out AudioCue cue)
        {
            cue = null;
            if (string.IsNullOrWhiteSpace(cueId))
                return false;

            if (catalog == null)
                LoadCatalogFallback();

            bool found = catalog != null && catalog.TryGet(cueId, out cue);
            if (!found && verboseMissingCueWarnings && warnedMissingCueIds.Add(cueId))
                Debug.LogWarning($"[AudioService] 未找到声音事件：{cueId}");

            return found;
        }

        public AudioHandle Play(string cueId)
        {
            return Play(cueId, AudioPlayOptions.Global());
        }

        public AudioHandle PlayAt(string cueId, Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            return Play(cueId, AudioPlayOptions.At(position, volumeScale, pitchScale));
        }

        public AudioHandle PlayAttached(
            string cueId,
            Transform target,
            float volumeScale = 1f,
            float pitchScale = 1f)
        {
            return Play(cueId, AudioPlayOptions.Attached(target, volumeScale, pitchScale));
        }

        public AudioHandle Play(string cueId, AudioPlayOptions options)
        {
            return TryGetCue(cueId, out AudioCue cue)
                ? Play(cue, options)
                : AudioHandle.Invalid;
        }

        public AudioHandle Play(AudioCue cue, AudioPlayOptions options)
        {
            if (cue == null)
                return AudioHandle.Invalid;

            options.Normalize();
            AudioClip clip = cue.SelectClip();
            if (clip == null)
            {
                if (verboseMissingCueWarnings && warnedMissingCueIds.Add(cue.Id + "#clips"))
                    Debug.LogWarning($"[AudioService] 声音事件 {cue.Id} 没有可播放的 AudioClip", cue);
                return AudioHandle.Invalid;
            }

            float now = Time.unscaledTime;
            if (cue.Cooldown > 0f &&
                lastPlayTime.TryGetValue(cue.Id, out float previousTime) &&
                now - previousTime < cue.Cooldown)
            {
                return AudioHandle.Invalid;
            }

            int sameCueCount = 0;
            Voice oldestSameCue = null;
            for (int i = 0; i < activeVoices.Count; i++)
            {
                Voice active = activeVoices[i];
                if (active.Cue != cue)
                    continue;

                sameCueCount++;
                if (oldestSameCue == null || active.StartedAt < oldestSameCue.StartedAt)
                    oldestSameCue = active;
            }

            if (sameCueCount >= cue.MaxInstances)
            {
                if (cue.ConcurrencyPolicy == AudioConcurrencyPolicy.RejectNew)
                    return AudioHandle.Invalid;

                ReleaseVoice(oldestSameCue);
            }

            Voice voice = AcquireVoice(cue.Priority);
            if (voice == null)
                return AudioHandle.Invalid;

            int voiceId = nextVoiceId++;
            if (nextVoiceId <= 0)
                nextVoiceId = 1;

            voice.Id = voiceId;
            voice.Cue = cue;
            voice.FollowTarget = options.FollowTarget;
            voice.StartedAt = now;
            voice.BaseGain = Mathf.Max(0f, cue.Volume.Sample() * options.VolumeScale);
            voice.Fade = options.FadeIn > 0f ? 0f : 1f;
            voice.FadeSpeed = options.FadeIn > 0f ? 1f / options.FadeIn : 0f;
            voice.Stopping = false;

            Transform voiceTransform = voice.GameObject.transform;
            if (options.FollowTarget != null)
                voiceTransform.position = options.FollowTarget.position;
            else if (options.HasWorldPosition)
                voiceTransform.position = options.WorldPosition;
            else
                voiceTransform.localPosition = Vector3.zero;

            AudioSource source = voice.Source;
            source.clip = clip;
            source.loop = options.OverrideLoop ? options.Loop : cue.Loop;
            source.pitch = Mathf.Clamp(cue.Pitch.Sample() * options.PitchScale, 0.01f, 3f);
            source.spatialBlend = cue.SpatialBlend;
            source.minDistance = cue.MinDistance;
            source.maxDistance = cue.MaxDistance;
            source.priority = cue.Priority;
            source.rolloffMode = cue.RolloffMode;
            source.outputAudioMixerGroup = ResolveOutput(cue);
            source.dopplerLevel = 0f;
            ApplyVolume(voice);

            activeVoices.Add(voice);
            lastPlayTime[cue.Id] = now;
            source.Play();
            return new AudioHandle(voiceId);
        }

        public void Stop(AudioHandle handle, float fadeOut = 0f)
        {
            Voice voice = FindVoice(handle);
            if (voice == null)
                return;

            if (fadeOut <= 0f)
            {
                ReleaseVoice(voice);
                return;
            }

            voice.Stopping = true;
            voice.FadeSpeed = 1f / fadeOut;
        }

        public void StopAll(float fadeOut = 0f)
        {
            for (int i = activeVoices.Count - 1; i >= 0; i--)
                Stop(new AudioHandle(activeVoices[i].Id), fadeOut);
        }

        public void StopBus(AudioBus bus, float fadeOut = 0f)
        {
            for (int i = activeVoices.Count - 1; i >= 0; i--)
            {
                Voice voice = activeVoices[i];
                if (voice.Cue != null && voice.Cue.Bus == bus)
                    Stop(new AudioHandle(voice.Id), fadeOut);
            }
        }

        public void SetMasterVolume(float value, bool save = true)
        {
            userSettings.Master = Mathf.Clamp01(value);
            ApplySettings(save);
        }

        public void SetBusVolume(AudioBus bus, float value, bool save = true)
        {
            userSettings.SetBusVolume(bus, value);
            ApplySettings(save);
        }

        public void SetMuted(bool value, bool save = true)
        {
            userSettings.Muted = value;
            ApplySettings(save);
        }

        public void ResetUserSettings()
        {
            userSettings = new AudioUserSettings();
            ApplySettings(true);
        }

        public static bool IsHandlePlaying(AudioHandle handle)
        {
            return instance != null && instance.FindVoice(handle) != null;
        }

        public static void TryStopHandle(AudioHandle handle, float fadeOut)
        {
            if (instance != null)
                instance.Stop(handle, fadeOut);
        }

        private void LoadConfiguration()
        {
            if (runtimeConfig == null)
                runtimeConfig = Resources.Load<AudioRuntimeConfig>(RuntimeConfigResourcePath);

            if (runtimeConfig != null)
            {
                maxVoices = runtimeConfig.MaxVoices;
                if (catalog == null)
                    catalog = runtimeConfig.Catalog;
            }

            maxVoices = Mathf.Max(1, maxVoices);
            if (catalog == null)
                LoadCatalogFallback();
            else
                catalog.RebuildIndex();
        }

        private void LoadCatalogFallback()
        {
            catalog = Resources.Load<AudioCatalog>(CatalogResourcePath);
            if (catalog == null)
                catalog = AudioCatalog.CreateRuntimeCatalogFromResources();
            catalog.RebuildIndex();
        }

        private AudioMixerGroup ResolveOutput(AudioCue cue)
        {
            if (cue.OutputOverride != null)
                return cue.OutputOverride;
            return runtimeConfig != null ? runtimeConfig.GetOutput(cue.Bus) : null;
        }

        private void Prewarm(int count)
        {
            count = Mathf.Clamp(count, 0, maxVoices);
            for (int i = totalVoices; i < count; i++)
                availableVoices.Push(CreateVoice());
        }

        private Voice AcquireVoice(int incomingPriority)
        {
            if (availableVoices.Count > 0)
                return availableVoices.Pop();

            if (totalVoices < maxVoices)
                return CreateVoice();

            Voice candidate = null;
            for (int i = 0; i < activeVoices.Count; i++)
            {
                Voice active = activeVoices[i];
                if (active.Source.priority < incomingPriority)
                    continue;

                if (candidate == null ||
                    active.Source.priority > candidate.Source.priority ||
                    (active.Source.priority == candidate.Source.priority && active.StartedAt < candidate.StartedAt))
                {
                    candidate = active;
                }
            }

            if (candidate == null)
                return null;

            ReleaseVoice(candidate);
            return availableVoices.Pop();
        }

        private Voice CreateVoice()
        {
            GameObject voiceObject = new GameObject($"Voice_{totalVoices:00}");
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            totalVoices++;

            return new Voice
            {
                GameObject = voiceObject,
                Source = source
            };
        }

        private Voice FindVoice(AudioHandle handle)
        {
            if (!handle.IsValid)
                return null;

            for (int i = 0; i < activeVoices.Count; i++)
            {
                if (activeVoices[i].Id == handle.VoiceId)
                    return activeVoices[i];
            }

            return null;
        }

        private void ReleaseVoice(Voice voice)
        {
            if (voice == null || !activeVoices.Remove(voice))
                return;

            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Source.outputAudioMixerGroup = null;
            voice.FollowTarget = null;
            voice.Cue = null;
            voice.Id = 0;
            voice.Fade = 1f;
            voice.FadeSpeed = 0f;
            voice.Stopping = false;
            voice.GameObject.transform.SetParent(transform, false);
            voice.GameObject.transform.localPosition = Vector3.zero;
            availableVoices.Push(voice);
        }

        private void ApplySettings(bool save)
        {
            userSettings.Clamp();
            for (int i = 0; i < activeVoices.Count; i++)
                ApplyVolume(activeVoices[i]);

            if (save && (runtimeConfig == null || runtimeConfig.PersistUserSettings))
                SaveUserSettings();

            SettingsChanged?.Invoke(userSettings);
        }

        private void ApplyVolume(Voice voice)
        {
            if (voice == null || voice.Cue == null)
                return;

            float settingsGain = userSettings.Muted
                ? 0f
                : userSettings.Master * userSettings.GetBusVolume(voice.Cue.Bus);
            voice.Source.volume = Mathf.Clamp01(voice.BaseGain * voice.Fade * settingsGain);
        }

        private void LoadUserSettings()
        {
            userSettings = new AudioUserSettings();
            if (runtimeConfig != null && !runtimeConfig.PersistUserSettings)
                return;

            userSettings.Master = PlayerPrefs.GetFloat(SettingsKeyPrefix + "Master", userSettings.Master);
            userSettings.Music = PlayerPrefs.GetFloat(SettingsKeyPrefix + "Music", userSettings.Music);
            userSettings.Sfx = PlayerPrefs.GetFloat(SettingsKeyPrefix + "Sfx", userSettings.Sfx);
            userSettings.UI = PlayerPrefs.GetFloat(SettingsKeyPrefix + "UI", userSettings.UI);
            userSettings.Ambient = PlayerPrefs.GetFloat(SettingsKeyPrefix + "Ambient", userSettings.Ambient);
            userSettings.Voice = PlayerPrefs.GetFloat(SettingsKeyPrefix + "Voice", userSettings.Voice);
            userSettings.Muted = PlayerPrefs.GetInt(SettingsKeyPrefix + "Muted", 0) != 0;
            userSettings.Clamp();
        }

        private void SaveUserSettings()
        {
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "Master", userSettings.Master);
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "Music", userSettings.Music);
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "Sfx", userSettings.Sfx);
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "UI", userSettings.UI);
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "Ambient", userSettings.Ambient);
            PlayerPrefs.SetFloat(SettingsKeyPrefix + "Voice", userSettings.Voice);
            PlayerPrefs.SetInt(SettingsKeyPrefix + "Muted", userSettings.Muted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
