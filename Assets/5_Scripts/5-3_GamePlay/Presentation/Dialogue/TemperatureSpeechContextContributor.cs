using System;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 独立读取本地玩家最终体温与既有冷/热伤阈值，向 JSON 台词提供进入、离开事件事实。
    /// 默认连续稳定 3 秒、双向共用 20 秒冷却、恢复滞回 0.5℃；不修改体温或伤害。
    /// </summary>
    public sealed class TemperatureSpeechContextContributor : ICharacterSpeechContextContributor
    {
        #region 配置与注册

        public const string SettingsResourcePath = "Dialogue/TemperatureFeedback";
        private readonly TemperatureSpeechTransitionTracker tracker = new();
        private readonly TemperatureSpeechSettings settings;
        public int ContextOrder => 170;

        private TemperatureSpeechContextContributor(TemperatureSpeechSettings settings)
        {
            this.settings = settings;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => CharacterSpeechContributorRegistry.Register(
            "flatworld.temperature", Create);

        /// <summary>独立加载反馈配置，错误配置直接报告，不悄悄替换为另一套阈值。</summary>
        public static TemperatureSpeechSettings LoadSettings()
        {
            TextAsset asset = Resources.Load<TextAsset>(SettingsResourcePath);
            if (asset == null)
                throw new InvalidOperationException("缺少 Dialogue/TemperatureFeedback.json");
            TemperatureSpeechSettings loaded = JsonConvert.DeserializeObject<TemperatureSpeechSettings>(asset.text);
            if (loaded == null)
                throw new InvalidOperationException("体温反馈配置为空。");
            loaded.Validate();
            return loaded;
        }

        private static ICharacterSpeechContextContributor Create() =>
            new TemperatureSpeechContextContributor(LoadSettings());

        #endregion

        #region 权威状态观察

        /// <summary>只消费本地角色已有体温状态；客户端由复制模块数据驱动，不另算玩法。</summary>
        public void Contribute(CharacterSpeechContext context)
        {
            Player player = context.Actor != null ? context.Actor.GetComponentInParent<Player>() : null;
            if (player == null || !player.IsLocalProfile)
                return;
            Mod_Temperature temperature = player.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
            if (temperature == null)
                return;
            tracker.Tick(temperature.Data.CurrentTemperature,
                temperature.Data.ColdDamageStart + settings.ColdThresholdOffset,
                temperature.Data.HotDamageStart + settings.HotThresholdOffset,
                context.RequestedAt, settings);
            context.SetFact(CharacterSpeechFacts.TemperatureTransition, tracker.Transition);
        }

        #endregion
    }

    /// <summary>温度反馈专用 JSON 配置，阈值偏移只影响提示，不改变冷/热伤害边界。</summary>
    public sealed class TemperatureSpeechSettings
    {
        #region 配置

        public float ColdThresholdOffset; // 相对既有冷伤阈值的偏移
        public float HotThresholdOffset; // 相对既有热伤阈值的偏移
        public float Hysteresis; // 恢复时须越过的温差
        public float DebounceSeconds; // 进入和离开都需连续满足的秒数
        public float CooldownSeconds; // 任意两次温度提示的最小间隔

        /// <summary>拒绝非有限、负数和零去抖配置。</summary>
        public void Validate()
        {
            if (!IsFinite(ColdThresholdOffset) || !IsFinite(HotThresholdOffset) ||
                !IsFinite(Hysteresis) || !IsFinite(DebounceSeconds) || !IsFinite(CooldownSeconds) ||
                Hysteresis < 0f || DebounceSeconds <= 0f || CooldownSeconds < 0f)
                throw new InvalidOperationException("体温提示配置包含非法数值。");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }

    /// <summary>可确定性诊断的双向阈值状态机；冷却期间只保留仍然连续成立的候选状态。</summary>
    public sealed class TemperatureSpeechTransitionTracker
    {
        #region 状态与推进

        private enum ThermalState { Normal, Cold, Hot }
        private ThermalState state;
        private ThermalState candidate;
        private float candidateSince;
        private float nextAllowedAt = float.NegativeInfinity;
        public string Transition { get; private set; } = string.Empty;

        /// <summary>显式时间输入，不依赖 Unity 帧、真实等待或随机数。</summary>
        public bool Tick(float temperature, float coldThreshold, float hotThreshold,
            float now, TemperatureSpeechSettings settings)
        {
            ThermalState next = ResolveState(temperature, coldThreshold, hotThreshold, settings.Hysteresis);
            if (next == state)
            {
                candidate = state;
                candidateSince = now;
                return false;
            }
            if (candidate != next)
            {
                candidate = next;
                candidateSince = now;
                return false;
            }
            if (now - candidateSince < settings.DebounceSeconds || now < nextAllowedAt)
                return false;

            Transition = next == ThermalState.Cold ? "ColdEnter" :
                next == ThermalState.Hot ? "HotEnter" :
                state == ThermalState.Cold ? "ColdExit" : "HotExit";
            state = next;
            nextAllowedAt = now + settings.CooldownSeconds;
            return true;
        }

        /// <summary>先退出旧异常，再进入另一异常，保证两类恢复提示都有独立边界。</summary>
        private ThermalState ResolveState(float temperature, float cold, float hot, float hysteresis)
        {
            if (state == ThermalState.Cold)
                return temperature >= cold + hysteresis ? ThermalState.Normal : ThermalState.Cold;
            if (state == ThermalState.Hot)
                return temperature <= hot - hysteresis ? ThermalState.Normal : ThermalState.Hot;
            return temperature < cold ? ThermalState.Cold :
                temperature > hot ? ThermalState.Hot : ThermalState.Normal;
        }

        #endregion
    }
}
