using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>
    /// Authoritative global event scheduler. Definitions are modular JSON files; only runtime
    /// progress and active action state are stored in the world save.
    /// </summary>
    public sealed class GameEventManager : SingletonAutoMono<GameEventManager>
    {
        private const int CurrentSaveDataVersion = 2;

        private readonly List<GameEventDefinition> definitions = new();
        private readonly Dictionary<string, GameEventDefinition> definitionsById =
            new(StringComparer.Ordinal);
        private readonly List<GameEventOccurrence> occurrenceBuffer = new(8);

        private GameManager boundGameManager;
        private DayTimeSystem subscribedTimeSystem;
        private GameEventSaveData runtimeData;
        private bool worldActive;

        public event Action<GameEventRuntimeNotification> EventStarted;
        public event Action<GameEventRuntimeNotification> EventEnded;
        public event Action<string, JObject> ConfiguredSignalRaised;

        public IReadOnlyList<GameEventDefinition> Definitions => definitions;
        public IReadOnlyList<ActiveGameEventSaveData> ActiveEvents =>
            runtimeData?.ActiveEvents is { } activeEvents
                ? activeEvents
                : Array.Empty<ActiveGameEventSaveData>();

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
            GameEventExtensionRegistry.EnsureBuiltInsRegistered();
            ReloadConfiguration();
            BindGameManager();
        }

        private void Start()
        {
            BindGameManager();
            if (boundGameManager != null && boundGameManager.IsInGameWorld && !worldActive)
                OnGameWorldEnter();
        }

        private void Update()
        {
            BindGameManager();
            if (worldActive)
                SubscribeTimeSystem();
        }

        public GameEventConfigLoadResult ReloadConfiguration()
        {
            GameEventExtensionRegistry.EnsureBuiltInsRegistered();
            GameEventConfigLoadResult loadResult = GameEventConfigLoader.LoadFromResources();
            definitions.Clear();
            definitionsById.Clear();

            for (int i = 0; i < loadResult.Definitions.Count; i++)
            {
                GameEventDefinition definition = loadResult.Definitions[i];
                if (!ValidateRegisteredExtensions(definition, out string error))
                {
                    Debug.LogError(
                        $"[GameEventConfig] file='{definition.SourceName}', event='{definition.Id}': {error}");
                    continue;
                }

                definitions.Add(definition);
                definitionsById.Add(definition.Id, definition);
            }

            return loadResult;
        }

        public bool TryTriggerNow(string eventId, bool ignoreConditions = false)
        {
            return TryTriggerNowInternal(eventId, ignoreConditions, ignoreRestrictions: false);
        }

        public bool TryForceTriggerNow(string eventId)
        {
            return TryTriggerNowInternal(eventId, ignoreConditions: true, ignoreRestrictions: true);
        }

        private bool TryTriggerNowInternal(
            string eventId,
            bool ignoreConditions,
            bool ignoreRestrictions)
        {
            if (!worldActive || !GameNetwork.HasStateAuthority ||
                string.IsNullOrWhiteSpace(eventId) ||
                !definitionsById.TryGetValue(eventId.Trim(), out GameEventDefinition definition) ||
                !TryGetCurrentClock(
                    out string activeWorldKey,
                    out _,
                    out TimeData timeData))
            {
                return false;
            }

            float totalTime = timeData.GetTotalGameTime();
            int dayNumber = Mathf.FloorToInt(totalTime / Mathf.Max(1f, timeData.DayLength)) + 1;
            GameEventOccurrence occurrence = new(totalTime, dayNumber, "manual");
            return TryStartEvent(
                definition,
                occurrence,
                activeWorldKey,
                totalTime,
                timeData.DayLength,
                ignoreConditions,
                ignoreRestrictions);
        }

        public bool CancelEvent(string eventId)
        {
            if (runtimeData?.ActiveEvents == null || string.IsNullOrWhiteSpace(eventId))
                return false;

            for (int i = runtimeData.ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveGameEventSaveData activeEvent = runtimeData.ActiveEvents[i];
                if (activeEvent != null &&
                    string.Equals(activeEvent.EventId, eventId, StringComparison.Ordinal))
                {
                    EndActiveEvent(activeEvent, cancelled: true, GetCurrentTotalTime());
                    return true;
                }
            }

            return false;
        }

        public bool IsEventActive(string eventId)
        {
            if (runtimeData?.ActiveEvents == null || string.IsNullOrWhiteSpace(eventId))
                return false;

            for (int i = 0; i < runtimeData.ActiveEvents.Count; i++)
            {
                if (string.Equals(runtimeData.ActiveEvents[i]?.EventId, eventId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        internal void RaiseConfiguredSignal(string signal, JObject payload)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return;

            ConfiguredSignalRaised?.Invoke(
                signal.Trim(),
                payload?.DeepClone() as JObject ?? new JObject());
        }

        private void BindGameManager()
        {
            GameManager current = GameManager.Instance;
            if (ReferenceEquals(boundGameManager, current))
                return;

            if (boundGameManager != null)
            {
                boundGameManager.Event_GameWorldEnter -= OnGameWorldEnter;
                boundGameManager.Event_GameWorldExit -= OnGameWorldExit;
            }

            boundGameManager = current;
            if (boundGameManager != null)
            {
                boundGameManager.Event_GameWorldEnter += OnGameWorldEnter;
                boundGameManager.Event_GameWorldExit += OnGameWorldExit;
            }
        }

        private void OnGameWorldEnter()
        {
            worldActive = true;
            BindSaveData(SaveDataMgr.Instance?.SaveData);
            SubscribeTimeSystem();
            if (GameNetwork.HasStateAuthority)
                ResumeActiveEvents();
        }

        private void OnGameWorldExit()
        {
            worldActive = false;
            UnsubscribeTimeSystem();
            runtimeData = null;
        }

        private void BindSaveData(GameSaveData saveData)
        {
            if (saveData == null)
            {
                runtimeData = new GameEventSaveData();
                return;
            }

            saveData.GameEventData ??= new GameEventSaveData();
            runtimeData = saveData.GameEventData;
            runtimeData.DataVersion = Mathf.Max(CurrentSaveDataVersion, runtimeData.DataVersion);
            runtimeData.EventProgress ??= new Dictionary<string, GameEventProgressSaveData>();
            runtimeData.ActiveEvents ??= new List<ActiveGameEventSaveData>();

            List<string> nullProgressKeys = null;
            foreach (KeyValuePair<string, GameEventProgressSaveData> pair in runtimeData.EventProgress)
            {
                if (pair.Value == null)
                {
                    nullProgressKeys ??= new List<string>();
                    nullProgressKeys.Add(pair.Key);
                    continue;
                }

                pair.Value.TriggerRuntimeDataJson ??= string.Empty;
            }

            if (nullProgressKeys != null)
            {
                for (int i = 0; i < nullProgressKeys.Count; i++)
                    runtimeData.EventProgress.Remove(nullProgressKeys[i]);
            }

            for (int i = runtimeData.ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveGameEventSaveData activeEvent = runtimeData.ActiveEvents[i];
                if (activeEvent == null || string.IsNullOrWhiteSpace(activeEvent.EventId))
                {
                    runtimeData.ActiveEvents.RemoveAt(i);
                    continue;
                }

                activeEvent.ActionStates ??= new List<GameEventActionRuntimeSaveData>();
                activeEvent.SourceWorldKey ??= string.Empty;
                activeEvent.TriggerPayloadJson ??= string.Empty;
            }
        }

        private void SubscribeTimeSystem()
        {
            DayTimeSystem current = DayTimeSystem.Instance;
            if (ReferenceEquals(subscribedTimeSystem, current))
                return;

            UnsubscribeTimeSystem();
            subscribedTimeSystem = current;
            if (subscribedTimeSystem != null)
                subscribedTimeSystem.TimeAdvanced += HandleTimeAdvanced;
        }

        private void UnsubscribeTimeSystem()
        {
            if (subscribedTimeSystem != null)
                subscribedTimeSystem.TimeAdvanced -= HandleTimeAdvanced;
            subscribedTimeSystem = null;
        }

        private void HandleTimeAdvanced(string timeSourceSceneName, float oldTotalTime, float newTotalTime)
        {
            if (!worldActive || !GameNetwork.HasStateAuthority ||
                newTotalTime <= oldTotalTime ||
                !IsRelevantTimeSource(timeSourceSceneName) ||
                !TryGetCurrentClock(
                    out string activeWorldKey,
                    out string resolvedTimeSource,
                    out TimeData timeData))
            {
                return;
            }

            GameEventTriggerContext triggerContext = new(
                resolvedTimeSource,
                activeWorldKey,
                oldTotalTime,
                newTotalTime,
                timeData.DayLength,
                SaveDataMgr.Instance?.SaveData?.Seed ?? 1);
            EvaluateScheduledEvents(triggerContext);
            TickActiveEvents(activeWorldKey, newTotalTime, timeData.DayLength);
        }

        private void EvaluateScheduledEvents(GameEventTriggerContext context)
        {
            if (runtimeData == null)
                return;

            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                GameEventDefinition definition = definitions[definitionIndex];
                if (!GameEventExtensionRegistry.TryGetTrigger(
                        definition.Trigger.Type,
                        out IGameEventTriggerHandler triggerHandler))
                {
                    continue;
                }

                GameEventProgressSaveData progress = GetOrCreateProgress(definition.Id);
                occurrenceBuffer.Clear();
                try
                {
                    triggerHandler.CollectOccurrences(
                        context,
                        definition,
                        definition.Trigger.Parameters,
                        progress,
                        occurrenceBuffer);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[GameEvent] Trigger '{definition.Id}' failed: {exception}");
                    continue;
                }

                for (int occurrenceIndex = 0;
                     occurrenceIndex < occurrenceBuffer.Count;
                     occurrenceIndex++)
                {
                    TryStartEvent(
                        definition,
                        occurrenceBuffer[occurrenceIndex],
                        context.ActiveWorldKey,
                        context.NewTotalTime,
                        context.DayLength,
                        ignoreConditions: false);
                }
            }
        }

        private bool TryStartEvent(
            GameEventDefinition definition,
            GameEventOccurrence occurrence,
            string activeWorldKey,
            float currentTotalTime,
            float dayLength,
            bool ignoreConditions,
            bool ignoreRestrictions = false)
        {
            if (definition == null || runtimeData == null || IsEventActive(definition.Id))
                return false;

            GameEventProgressSaveData progress = GetOrCreateProgress(definition.Id);
            if (!ignoreRestrictions && definition.OncePerWorld && progress.TriggerCount > 0)
                return false;

            float normalizedDayLength = Mathf.Max(1f, dayLength);
            if (!ignoreRestrictions && definition.CooldownDays > 0f &&
                progress.LastTriggeredTotalTime >= 0f &&
                occurrence.TriggeredTotalTime - progress.LastTriggeredTotalTime <
                definition.CooldownDays * normalizedDayLength)
            {
                return false;
            }

            if (!ignoreRestrictions && HasConflict(definition))
                return false;

            if (!ignoreConditions &&
                !EvaluateConditions(
                    definition,
                    occurrence,
                    activeWorldKey,
                    currentTotalTime,
                    normalizedDayLength))
            {
                return false;
            }

            progress.TriggerCount++;
            progress.LastTriggeredDayNumber = occurrence.DayNumber;
            progress.LastTriggeredTotalTime = occurrence.TriggeredTotalTime;

            float endTotalTime = definition.DurationDays > 0f
                ? occurrence.TriggeredTotalTime + definition.DurationDays * normalizedDayLength
                : 0f;
            if (endTotalTime > 0f && endTotalTime <= currentTotalTime + 0.0001f)
            {
                Debug.Log(
                    $"[GameEvent] '{definition.DisplayName}' was crossed by a time jump and expired without runtime actions.");
                return false;
            }

            ActiveGameEventSaveData activeEvent = new()
            {
                EventId = definition.Id,
                SourceWorldKey = activeWorldKey,
                StartedTotalTime = occurrence.TriggeredTotalTime,
                EndTotalTime = endTotalTime,
                TriggerDayNumber = occurrence.DayNumber,
                ActionStates = new List<GameEventActionRuntimeSaveData>(),
                TriggerPayloadJson = occurrence.PayloadJson ?? string.Empty
            };
            EnsureActionStates(definition, activeEvent);
            runtimeData.ActiveEvents.Add(activeEvent);

            for (int i = 0; i < definition.Actions.Count; i++)
                BeginAction(definition, definition.Actions[i], activeEvent, currentTotalTime, normalizedDayLength);

            Debug.Log($"[GameEvent] Started '{definition.DisplayName}' ({definition.Id}).");
            EventStarted?.Invoke(new GameEventRuntimeNotification(definition, activeEvent));

            if (definition.DurationDays <= 0f && AreAllActionsCompleted(activeEvent))
                EndActiveEvent(activeEvent, cancelled: false, currentTotalTime);

            return true;
        }

        private bool EvaluateConditions(
            GameEventDefinition definition,
            GameEventOccurrence occurrence,
            string activeWorldKey,
            float currentTotalTime,
            float dayLength)
        {
            GameEventEvaluationContext context = new(
                this,
                definition,
                occurrence,
                activeWorldKey,
                currentTotalTime,
                dayLength);

            for (int i = 0; i < definition.Conditions.Count; i++)
            {
                GameEventExtensionDefinition condition = definition.Conditions[i];
                if (!GameEventExtensionRegistry.TryGetCondition(
                        condition.Type,
                        out IGameEventConditionEvaluator evaluator) ||
                    !evaluator.Evaluate(context, condition.Parameters, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasConflict(GameEventDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.ConflictGroup) || runtimeData?.ActiveEvents == null)
                return false;

            for (int i = 0; i < runtimeData.ActiveEvents.Count; i++)
            {
                ActiveGameEventSaveData activeEvent = runtimeData.ActiveEvents[i];
                if (activeEvent != null &&
                    definitionsById.TryGetValue(activeEvent.EventId, out GameEventDefinition activeDefinition) &&
                    string.Equals(
                        activeDefinition.ConflictGroup,
                        definition.ConflictGroup,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResumeActiveEvents()
        {
            if (runtimeData?.ActiveEvents == null ||
                !TryGetCurrentClock(
                    out string activeWorldKey,
                    out _,
                    out TimeData timeData))
            {
                return;
            }

            float currentTotalTime = timeData.GetTotalGameTime();
            float dayLength = Mathf.Max(1f, timeData.DayLength);
            for (int i = runtimeData.ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveGameEventSaveData activeEvent = runtimeData.ActiveEvents[i];
                if (activeEvent == null ||
                    !definitionsById.TryGetValue(activeEvent.EventId, out GameEventDefinition definition))
                {
                    runtimeData.ActiveEvents.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(activeEvent.SourceWorldKey))
                    activeEvent.SourceWorldKey = activeWorldKey;
                if (!string.Equals(activeEvent.SourceWorldKey, activeWorldKey, StringComparison.Ordinal))
                    continue;

                EnsureActionStates(definition, activeEvent);
                for (int actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
                {
                    GameEventActionDefinition action = definition.Actions[actionIndex];
                    GameEventActionRuntimeSaveData state = FindActionState(activeEvent, action.Id);
                    if (state == null)
                        continue;

                    if (!state.Started)
                        BeginAction(definition, action, activeEvent, currentTotalTime, dayLength);
                    else
                        ResumeAction(definition, action, activeEvent, state, currentTotalTime, dayLength);
                }

                if (activeEvent.EndTotalTime > 0f &&
                    activeEvent.EndTotalTime <= currentTotalTime + 0.0001f)
                {
                    EndActiveEvent(activeEvent, cancelled: false, currentTotalTime);
                }
            }

            TickActiveEvents(activeWorldKey, currentTotalTime, dayLength);
        }

        private void TickActiveEvents(string activeWorldKey, float currentTotalTime, float dayLength)
        {
            if (runtimeData?.ActiveEvents == null)
                return;

            for (int i = runtimeData.ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveGameEventSaveData activeEvent = runtimeData.ActiveEvents[i];
                if (activeEvent == null ||
                    !definitionsById.TryGetValue(activeEvent.EventId, out GameEventDefinition definition))
                {
                    runtimeData.ActiveEvents.RemoveAt(i);
                    continue;
                }

                if (!string.Equals(activeEvent.SourceWorldKey, activeWorldKey, StringComparison.Ordinal))
                    continue;

                if (activeEvent.EndTotalTime > 0f &&
                    currentTotalTime >= activeEvent.EndTotalTime - 0.0001f)
                {
                    EndActiveEvent(activeEvent, cancelled: false, currentTotalTime);
                    continue;
                }

                EnsureActionStates(definition, activeEvent);
                for (int actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
                {
                    GameEventActionDefinition action = definition.Actions[actionIndex];
                    GameEventActionRuntimeSaveData state = FindActionState(activeEvent, action.Id);
                    if (state == null || state.Completed)
                        continue;

                    TickAction(definition, action, activeEvent, state, currentTotalTime, dayLength);
                }

                if (definition.DurationDays <= 0f && AreAllActionsCompleted(activeEvent))
                    EndActiveEvent(activeEvent, cancelled: false, currentTotalTime);
            }
        }

        private void BeginAction(
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            float currentTotalTime,
            float dayLength)
        {
            GameEventActionRuntimeSaveData state = FindActionState(activeEvent, action.Id);
            if (state == null || state.Started ||
                !GameEventExtensionRegistry.TryGetAction(action.Type, out IGameEventActionHandler handler))
            {
                return;
            }

            state.Started = true;
            ExecuteAction(
                definition,
                action,
                activeEvent,
                state,
                currentTotalTime,
                dayLength,
                context => handler.Begin(context, action.Parameters, state));
        }

        private void ResumeAction(
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            GameEventActionRuntimeSaveData state,
            float currentTotalTime,
            float dayLength)
        {
            if (!GameEventExtensionRegistry.TryGetAction(action.Type, out IGameEventActionHandler handler))
                return;

            try
            {
                handler.Resume(
                    CreateActionContext(
                        definition,
                        action,
                        activeEvent,
                        currentTotalTime,
                        dayLength),
                    action.Parameters,
                    state);
                state.LastError = string.Empty;
            }
            catch (Exception exception)
            {
                state.LastError = exception.Message;
                Debug.LogError(
                    $"[GameEvent] Resume action '{definition.Id}/{action.Id}' failed: {exception}");
            }
        }

        private void TickAction(
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            GameEventActionRuntimeSaveData state,
            float currentTotalTime,
            float dayLength)
        {
            if (!state.Started)
            {
                BeginAction(definition, action, activeEvent, currentTotalTime, dayLength);
                return;
            }

            if (!GameEventExtensionRegistry.TryGetAction(action.Type, out IGameEventActionHandler handler))
                return;

            ExecuteAction(
                definition,
                action,
                activeEvent,
                state,
                currentTotalTime,
                dayLength,
                context => handler.Tick(context, action.Parameters, state));
        }

        private void ExecuteAction(
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            GameEventActionRuntimeSaveData state,
            float currentTotalTime,
            float dayLength,
            Func<GameEventActionContext, GameEventActionStatus> operation)
        {
            try
            {
                GameEventActionStatus status = operation(CreateActionContext(
                    definition,
                    action,
                    activeEvent,
                    currentTotalTime,
                    dayLength));
                state.Completed = status == GameEventActionStatus.Completed;
                state.LastError = string.Empty;
            }
            catch (Exception exception)
            {
                state.Completed = true;
                state.LastError = exception.Message;
                Debug.LogError(
                    $"[GameEvent] Action '{definition.Id}/{action.Id}' failed and was isolated: {exception}");
            }
        }

        private void EndActiveEvent(
            ActiveGameEventSaveData activeEvent,
            bool cancelled,
            float currentTotalTime)
        {
            if (activeEvent == null || runtimeData?.ActiveEvents == null)
                return;

            if (!definitionsById.TryGetValue(activeEvent.EventId, out GameEventDefinition definition))
            {
                runtimeData.ActiveEvents.Remove(activeEvent);
                return;
            }

            float dayLength = TryGetCurrentClock(out _, out _, out TimeData timeData)
                ? Mathf.Max(1f, timeData.DayLength)
                : 1440f;
            EnsureActionStates(definition, activeEvent);
            for (int i = 0; i < definition.Actions.Count; i++)
            {
                GameEventActionDefinition action = definition.Actions[i];
                GameEventActionRuntimeSaveData state = FindActionState(activeEvent, action.Id);
                if (state == null || !state.Started ||
                    !GameEventExtensionRegistry.TryGetAction(action.Type, out IGameEventActionHandler handler))
                {
                    continue;
                }

                try
                {
                    handler.End(
                        CreateActionContext(
                            definition,
                            action,
                            activeEvent,
                            currentTotalTime,
                            dayLength),
                        action.Parameters,
                        state,
                        cancelled);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[GameEvent] End action '{definition.Id}/{action.Id}' failed: {exception}");
                }
            }

            runtimeData.ActiveEvents.Remove(activeEvent);
            Debug.Log($"[GameEvent] Ended '{definition.DisplayName}' ({definition.Id}).");
            EventEnded?.Invoke(new GameEventRuntimeNotification(definition, activeEvent));
        }

        private GameEventActionContext CreateActionContext(
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            float currentTotalTime,
            float dayLength)
        {
            return new GameEventActionContext(
                this,
                definition,
                action,
                activeEvent,
                activeEvent.SourceWorldKey,
                currentTotalTime,
                dayLength);
        }

        private static void EnsureActionStates(
            GameEventDefinition definition,
            ActiveGameEventSaveData activeEvent)
        {
            activeEvent.ActionStates ??= new List<GameEventActionRuntimeSaveData>();
            for (int i = activeEvent.ActionStates.Count - 1; i >= 0; i--)
            {
                if (activeEvent.ActionStates[i] == null ||
                    string.IsNullOrWhiteSpace(activeEvent.ActionStates[i].ActionId))
                {
                    activeEvent.ActionStates.RemoveAt(i);
                }
            }

            for (int actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
            {
                GameEventActionDefinition action = definition.Actions[actionIndex];
                if (FindActionState(activeEvent, action.Id) != null)
                    continue;

                activeEvent.ActionStates.Add(new GameEventActionRuntimeSaveData
                {
                    ActionId = action.Id,
                    RuntimeDataJson = string.Empty,
                    LastError = string.Empty
                });
            }
        }

        private static GameEventActionRuntimeSaveData FindActionState(
            ActiveGameEventSaveData activeEvent,
            string actionId)
        {
            if (activeEvent?.ActionStates == null)
                return null;

            for (int i = 0; i < activeEvent.ActionStates.Count; i++)
            {
                GameEventActionRuntimeSaveData state = activeEvent.ActionStates[i];
                if (state != null && string.Equals(state.ActionId, actionId, StringComparison.Ordinal))
                    return state;
            }

            return null;
        }

        private static bool AreAllActionsCompleted(ActiveGameEventSaveData activeEvent)
        {
            if (activeEvent?.ActionStates == null || activeEvent.ActionStates.Count == 0)
                return true;

            for (int i = 0; i < activeEvent.ActionStates.Count; i++)
            {
                if (activeEvent.ActionStates[i] != null && !activeEvent.ActionStates[i].Completed)
                    return false;
            }

            return true;
        }

        private GameEventProgressSaveData GetOrCreateProgress(string eventId)
        {
            runtimeData.EventProgress ??= new Dictionary<string, GameEventProgressSaveData>();
            if (!runtimeData.EventProgress.TryGetValue(eventId, out GameEventProgressSaveData progress) ||
                progress == null)
            {
                progress = new GameEventProgressSaveData();
                runtimeData.EventProgress[eventId] = progress;
            }

            return progress;
        }

        private static bool ValidateRegisteredExtensions(
            GameEventDefinition definition,
            out string error)
        {
            if (!GameEventExtensionRegistry.TryGetTrigger(
                    definition.Trigger.Type,
                    out IGameEventTriggerHandler trigger))
            {
                error = $"Unknown trigger type '{definition.Trigger.Type}'.";
                return false;
            }
            if (!trigger.Validate(definition.Trigger.Parameters, out error))
                return false;

            for (int i = 0; i < definition.Conditions.Count; i++)
            {
                GameEventExtensionDefinition condition = definition.Conditions[i];
                if (!GameEventExtensionRegistry.TryGetCondition(
                        condition.Type,
                        out IGameEventConditionEvaluator evaluator))
                {
                    error = $"Unknown condition type '{condition.Type}'.";
                    return false;
                }
                if (!evaluator.Validate(condition.Parameters, out error))
                {
                    error = $"Condition '{condition.Type}': {error}";
                    return false;
                }
            }

            for (int i = 0; i < definition.Actions.Count; i++)
            {
                GameEventActionDefinition action = definition.Actions[i];
                if (!GameEventExtensionRegistry.TryGetAction(
                        action.Type,
                        out IGameEventActionHandler handler))
                {
                    error = $"Unknown action type '{action.Type}'.";
                    return false;
                }
                if (!handler.Validate(action.Parameters, out error))
                {
                    error = $"Action '{action.Id}' ({action.Type}): {error}";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private bool IsRelevantTimeSource(string sceneName)
        {
            if (!TryGetCurrentClock(out _, out string resolvedSceneName, out _))
                return false;
            return string.Equals(sceneName, resolvedSceneName, StringComparison.Ordinal);
        }

        private static bool TryGetCurrentClock(
            out string activeWorldKey,
            out string resolvedSceneName,
            out TimeData timeData)
        {
            activeWorldKey = SceneManager.GetActiveScene().name;
            resolvedSceneName = activeWorldKey;
            timeData = null;
            return DayTimeSystem.Instance != null &&
                   DayTimeSystem.Instance.TryGetResolvedTimeData(
                       activeWorldKey,
                       out resolvedSceneName,
                       out timeData) &&
                   timeData != null;
        }

        private static float GetCurrentTotalTime()
        {
            return TryGetCurrentClock(out _, out _, out TimeData timeData)
                ? timeData.GetTotalGameTime()
                : 0f;
        }

        protected override void OnDestroy()
        {
            UnsubscribeTimeSystem();
            if (boundGameManager != null)
            {
                boundGameManager.Event_GameWorldEnter -= OnGameWorldEnter;
                boundGameManager.Event_GameWorldExit -= OnGameWorldExit;
                boundGameManager = null;
            }

            base.OnDestroy();
        }
    }
}
