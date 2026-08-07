using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 台词的重要程度。高优先级台词可以覆盖正在显示的低优先级气泡。
    /// </summary>
    public enum CharacterSpeechPriority
    {
        Ambient = 0,
        Need = 10,
        Critical = 20,
        Player = 25,
        Emergency = 30
    }

    public enum CharacterSpeechTrigger
    {
        Idle,
        StateChanged,
        External,
        Debug
    }

    /// <summary>
    /// 与显示方式无关的台词数据。未来大模型返回同一结构即可接入。
    /// </summary>
    [Serializable]
    public sealed class CharacterSpeechRequest
    {
        #region 显示内容

        public string Text;
        public string Topic;
        public CharacterSpeechPriority Priority;
        public float Duration;

        #endregion

        #region 配置来源

        /// <summary>配置条目的稳定 ID；外部直接发言时为空。</summary>
        public string SourceId;

        /// <summary>一次性台词的完成标记；仅在成功显示后写入玩家存档。</summary>
        public string CompletionFlag;

        #endregion

        public bool IsValid => !string.IsNullOrWhiteSpace(Text);

        public CharacterSpeechRequest()
        {
        }

        public CharacterSpeechRequest(
            string text,
            string topic,
            CharacterSpeechPriority priority,
            float duration = 0f)
        {
            Text = text;
            Topic = topic;
            Priority = priority;
            Duration = duration;
        }
    }

    /// <summary>
    /// 提供给台词生成器的只读角色上下文。
    /// Facts 使用稳定字符串键，便于以后直接转换成大模型提示词。
    /// </summary>
    public sealed class CharacterSpeechContext
    {
        private readonly Dictionary<string, string> facts =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public Transform Actor { get; }
        public CharacterSpeechTrigger Trigger { get; }
        public float RequestedAt { get; }
        public IReadOnlyDictionary<string, string> Facts => facts;

        public CharacterSpeechContext(
            Transform actor,
            CharacterSpeechTrigger trigger,
            float requestedAt)
        {
            Actor = actor;
            Trigger = trigger;
            RequestedAt = requestedAt;
        }

        public void SetFact(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            facts[key] = value ?? string.Empty;
        }

        public bool TryGetFact(string key, out string value)
        {
            return facts.TryGetValue(key, out value);
        }
    }

    /// <summary>
    /// 向上下文补充角色状态，例如饥饿、生命、天气或附近事件。
    /// </summary>
    public interface ICharacterSpeechContextContributor
    {
        int ContextOrder { get; }
        void Contribute(CharacterSpeechContext context);
    }

    /// <summary>
    /// 随机自言自语内容提供者。
    /// 回调形式允许未来接入异步大模型；回调必须切回 Unity 主线程。
    /// </summary>
    public interface ICharacterSpeechProvider
    {
        int ProviderOrder { get; }
        bool CanProvide(CharacterSpeechContext context);
        void RequestSpeech(
            CharacterSpeechContext context,
            Action<CharacterSpeechRequest> onCompleted);
    }

    /// <summary>
    /// 状态跨过阈值时立即触发台词，不必等待随机自言自语计时器。
    /// </summary>
    public interface ICharacterSpeechTriggerSource
    {
        CharacterSpeechRequest PollTriggeredSpeech(CharacterSpeechContext context);
    }

    public interface ICharacterSpeechPresenter
    {
        bool IsVisible { get; }
        CharacterSpeechPriority VisiblePriority { get; }
        bool Show(CharacterSpeechRequest request);
        void HideImmediate();
    }
}
