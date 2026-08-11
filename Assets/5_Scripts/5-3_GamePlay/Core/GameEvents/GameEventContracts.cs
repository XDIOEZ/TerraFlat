using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    public readonly struct GameEventOccurrence
    {
        public float TriggeredTotalTime { get; }
        public int DayNumber { get; }
        public string Cause { get; }
        public string PayloadJson { get; }

        public GameEventOccurrence(
            float triggeredTotalTime,
            int dayNumber,
            string cause,
            string payloadJson = null)
        {
            TriggeredTotalTime = triggeredTotalTime;
            DayNumber = dayNumber;
            Cause = cause ?? string.Empty;
            PayloadJson = payloadJson ?? string.Empty;
        }
    }

    public sealed class GameEventTriggerContext
    {
        public string TimeSourceSceneName { get; }
        public string ActiveWorldKey { get; }
        public float OldTotalTime { get; }
        public float NewTotalTime { get; }
        public float DayLength { get; }
        public int WorldSeed { get; }

        public GameEventTriggerContext(
            string timeSourceSceneName,
            string activeWorldKey,
            float oldTotalTime,
            float newTotalTime,
            float dayLength,
            int worldSeed)
        {
            TimeSourceSceneName = timeSourceSceneName ?? string.Empty;
            ActiveWorldKey = activeWorldKey ?? string.Empty;
            OldTotalTime = oldTotalTime;
            NewTotalTime = newTotalTime;
            DayLength = Mathf.Max(1f, dayLength);
            WorldSeed = worldSeed == 0 ? 1 : worldSeed;
        }
    }

    public sealed class GameEventEvaluationContext
    {
        public GameEventManager Manager { get; }
        public GameEventDefinition Definition { get; }
        public GameEventOccurrence Occurrence { get; }
        public string ActiveWorldKey { get; }
        public float CurrentTotalTime { get; }
        public float DayLength { get; }
        public JObject TriggerPayload { get; }

        public GameEventEvaluationContext(
            GameEventManager manager,
            GameEventDefinition definition,
            GameEventOccurrence occurrence,
            string activeWorldKey,
            float currentTotalTime,
            float dayLength)
        {
            Manager = manager;
            Definition = definition;
            Occurrence = occurrence;
            ActiveWorldKey = activeWorldKey ?? string.Empty;
            CurrentTotalTime = currentTotalTime;
            DayLength = Mathf.Max(1f, dayLength);
            TriggerPayload = ParsePayload(occurrence.PayloadJson);
        }

        private static JObject ParsePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();

            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return new JObject();
            }
        }
    }

    public sealed class GameEventActionContext
    {
        /// <summary>GM 强制触发写入的运行时标记键，不改变正式事件配置参数。</summary>
        public const string GmForcePayloadKey = "__gmForce";

        public GameEventManager Manager { get; }
        public GameEventDefinition Definition { get; }
        public GameEventActionDefinition Action { get; }
        public ActiveGameEventSaveData ActiveEvent { get; }
        public string ActiveWorldKey { get; }
        public float CurrentTotalTime { get; }
        public float DayLength { get; }
        public JObject TriggerPayload { get; }

        /// <summary>当前事件是否由 GM 面板强制触发；该标记会随事件触发载荷保存。</summary>
        public bool IsGmForced => TriggerPayload.Value<bool?>(GmForcePayloadKey) == true;

        public GameEventActionContext(
            GameEventManager manager,
            GameEventDefinition definition,
            GameEventActionDefinition action,
            ActiveGameEventSaveData activeEvent,
            string activeWorldKey,
            float currentTotalTime,
            float dayLength)
        {
            Manager = manager;
            Definition = definition;
            Action = action;
            ActiveEvent = activeEvent;
            ActiveWorldKey = activeWorldKey ?? string.Empty;
            CurrentTotalTime = currentTotalTime;
            DayLength = Mathf.Max(1f, dayLength);
            TriggerPayload = ParsePayload(activeEvent?.TriggerPayloadJson);
        }

        private static JObject ParsePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();

            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return new JObject();
            }
        }
    }

    public enum GameEventActionStatus
    {
        Running,
        Completed
    }

    public interface IGameEventTriggerHandler
    {
        string Type { get; }
        bool Validate(JObject parameters, out string error);
        void CollectOccurrences(
            GameEventTriggerContext context,
            GameEventDefinition definition,
            JObject parameters,
            GameEventProgressSaveData progress,
            List<GameEventOccurrence> results);
    }

    public interface IGameEventConditionEvaluator
    {
        string Type { get; }
        bool Validate(JObject parameters, out string error);
        bool Evaluate(
            GameEventEvaluationContext context,
            JObject parameters,
            out string failureReason);
    }

    public interface IGameEventActionHandler
    {
        string Type { get; }
        bool Validate(JObject parameters, out string error);
        GameEventActionStatus Begin(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state);
        void Resume(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state);
        GameEventActionStatus Tick(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state);
        void End(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state,
            bool cancelled);
    }

    public readonly struct GameEventRuntimeNotification
    {
        public string EventId { get; }
        public string DisplayName { get; }
        public string WorldKey { get; }
        public float StartedTotalTime { get; }
        public float EndTotalTime { get; }

        public GameEventRuntimeNotification(
            GameEventDefinition definition,
            ActiveGameEventSaveData activeEvent)
        {
            EventId = definition?.Id ?? activeEvent?.EventId ?? string.Empty;
            DisplayName = definition?.DisplayName ?? EventId;
            WorldKey = activeEvent?.SourceWorldKey ?? string.Empty;
            StartedTotalTime = activeEvent?.StartedTotalTime ?? 0f;
            EndTotalTime = activeEvent?.EndTotalTime ?? 0f;
        }
    }
}
