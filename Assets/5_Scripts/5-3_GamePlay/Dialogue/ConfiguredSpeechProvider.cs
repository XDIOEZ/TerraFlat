using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 根据 JSON 条目匹配 Facts，并提供空闲与状态变化两种自言自语请求。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ConfiguredSpeechProvider :
        MonoBehaviour,
        ICharacterSpeechProvider,
        ICharacterSpeechTriggerSource
    {
        [SerializeField] private int providerOrder = 1000;

        private readonly Dictionary<string, CharacterSpeechConfigEntry> entriesById =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> previousConditionStates =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> nextAllowedSpeechAt =
            new(StringComparer.Ordinal);

        private CharacterSoliloquyController controller;
        private CharacterSpeechCompletionStore completionStore;
        private List<CharacterSpeechConfigEntry> entries = new();
        private bool configLoaded;

        public int ProviderOrder => providerOrder;

        #region 生命周期

        private void Awake()
        {
            completionStore = new CharacterSpeechCompletionStore(this);
        }

        private void OnEnable()
        {
            controller = GetComponentInParent<CharacterSoliloquyController>();
            if (controller != null)
                controller.SpeechShown += HandleSpeechShown;
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.SpeechShown -= HandleSpeechShown;
            controller = null;
        }

        [ContextMenu("重新加载自言自语 JSON")]
        public void ReloadConfiguration()
        {
            configLoaded = false;
            EnsureConfigurationLoaded();
        }

        #endregion

        #region Provider

        public bool CanProvide(CharacterSpeechContext context)
        {
            EnsureConfigurationLoaded();
            for (int i = 0; i < entries.Count; i++)
            {
                CharacterSpeechConfigEntry entry = entries[i];
                if (SupportsTrigger(entry, CharacterSpeechTrigger.Idle) &&
                    IsAvailable(entry, context) &&
                    CharacterSpeechConditionEvaluator.EvaluateAll(entry, context))
                {
                    return true;
                }
            }

            return false;
        }

        public void RequestSpeech(
            CharacterSpeechContext context,
            Action<CharacterSpeechRequest> onCompleted)
        {
            EnsureConfigurationLoaded();
            List<CharacterSpeechConfigEntry> candidates = CollectCandidates(
                context,
                CharacterSpeechTrigger.Idle,
                updateStateCache: false);
            onCompleted?.Invoke(BuildRequest(SelectCandidate(candidates)));
        }

        public CharacterSpeechRequest PollTriggeredSpeech(CharacterSpeechContext context)
        {
            EnsureConfigurationLoaded();
            List<CharacterSpeechConfigEntry> candidates = CollectCandidates(
                context,
                CharacterSpeechTrigger.StateChanged,
                updateStateCache: true);
            return BuildRequest(SelectCandidate(candidates));
        }

        #endregion

        #region 候选选择

        private List<CharacterSpeechConfigEntry> CollectCandidates(
            CharacterSpeechContext context,
            CharacterSpeechTrigger trigger,
            bool updateStateCache)
        {
            List<CharacterSpeechConfigEntry> candidates = new();
            for (int i = 0; i < entries.Count; i++)
            {
                CharacterSpeechConfigEntry entry = entries[i];
                if (!SupportsTrigger(entry, trigger))
                    continue;

                bool conditionsMet = CharacterSpeechConditionEvaluator.EvaluateAll(entry, context);
                if (updateStateCache)
                {
                    bool wasMet = previousConditionStates.TryGetValue(entry.Id, out bool previous) && previous;
                    previousConditionStates[entry.Id] = conditionsMet;
                    if (!conditionsMet || wasMet)
                        continue;
                }
                else if (!conditionsMet)
                {
                    continue;
                }

                if (IsAvailable(entry, context))
                    candidates.Add(entry);
            }

            return candidates;
        }

        private CharacterSpeechConfigEntry SelectCandidate(List<CharacterSpeechConfigEntry> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            CharacterSpeechPriority highestPriority = candidates[0].Priority;
            int highestCount = 1;
            while (highestCount < candidates.Count &&
                   candidates[highestCount].Priority == highestPriority)
            {
                highestCount++;
            }

            return candidates[UnityEngine.Random.Range(0, highestCount)];
        }

        private static bool SupportsTrigger(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechTrigger trigger)
        {
            return entry.Triggers != null && entry.Triggers.Contains(trigger);
        }

        private bool IsAvailable(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechContext context)
        {
            if (entry.Once && completionStore.IsCompleted(entry.CompletionFlag))
                return false;

            return !nextAllowedSpeechAt.TryGetValue(entry.Id, out float nextAllowed) ||
                   context.RequestedAt >= nextAllowed;
        }

        private static CharacterSpeechRequest BuildRequest(CharacterSpeechConfigEntry entry)
        {
            if (entry == null || entry.Lines == null || entry.Lines.Count == 0)
                return null;

            string line = entry.Lines[UnityEngine.Random.Range(0, entry.Lines.Count)];
            return new CharacterSpeechRequest(
                line,
                entry.Topic,
                entry.Priority,
                entry.Duration)
            {
                SourceId = entry.Id,
                CompletionFlag = entry.Once ? entry.CompletionFlag : string.Empty
            };
        }

        #endregion

        #region 显示成功回写

        private void HandleSpeechShown(CharacterSpeechRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SourceId) ||
                !entriesById.TryGetValue(request.SourceId, out CharacterSpeechConfigEntry entry))
            {
                return;
            }

            nextAllowedSpeechAt[entry.Id] = Time.unscaledTime + Mathf.Max(0f, entry.Cooldown);
            if (entry.Once)
                completionStore.MarkCompleted(request.CompletionFlag);
        }

        #endregion

        #region 配置加载

        private void EnsureConfigurationLoaded()
        {
            if (configLoaded)
                return;

            configLoaded = true;
            CharacterSpeechConfigLoadResult result =
                CharacterSpeechConfigLoader.LoadFromResources(logIssues: true);
            entries = result.Entries;

            entriesById.Clear();
            previousConditionStates.Clear();
            nextAllowedSpeechAt.Clear();
            for (int i = 0; i < entries.Count; i++)
                entriesById[entries[i].Id] = entries[i];
        }

        #endregion
    }
}
