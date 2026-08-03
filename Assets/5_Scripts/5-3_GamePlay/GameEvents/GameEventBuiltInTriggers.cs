using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    [Serializable]
    public sealed class DayScheduleGameEventTriggerParameters
    {
        [JsonProperty("minimumDay")]
        public int MinimumDay = 1;

        [JsonProperty("repeatEveryDays")]
        public int RepeatEveryDays;

        [JsonProperty("timeOfDay")]
        public float TimeOfDay;

        [JsonProperty("chance")]
        public float Chance = 1f;
    }

    public sealed class DayScheduleGameEventTrigger : IGameEventTriggerHandler
    {
        private const int MaximumOccurrencesPerAdvance = 4096;

        public string Type => "day.schedule";

        public bool Validate(JObject parameters, out string error)
        {
            DayScheduleGameEventTriggerParameters value = Read(parameters);
            if (value.MinimumDay < 1)
            {
                error = "minimumDay must be at least 1.";
                return false;
            }

            if (value.RepeatEveryDays < 0)
            {
                error = "repeatEveryDays cannot be negative; use 0 for a one-shot schedule.";
                return false;
            }

            if (value.TimeOfDay < 0f)
            {
                error = "timeOfDay cannot be negative.";
                return false;
            }

            if (value.Chance < 0f || value.Chance > 1f)
            {
                error = "chance must be between 0 and 1.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void CollectOccurrences(
            GameEventTriggerContext context,
            GameEventDefinition definition,
            JObject parameters,
            GameEventProgressSaveData progress,
            List<GameEventOccurrence> results)
        {
            if (context == null || definition == null || progress == null || results == null)
                return;

            DayScheduleGameEventTriggerParameters value = Read(parameters);
            float dayLength = Mathf.Max(1f, context.DayLength);
            int firstDay = Mathf.Max(1, value.MinimumDay);
            int startDay = Mathf.Max(1, Mathf.FloorToInt(context.OldTotalTime / dayLength) + 1);
            int endDay = Mathf.Max(startDay, Mathf.FloorToInt(context.NewTotalTime / dayLength) + 1);
            float triggerTimeInDay = Mathf.Repeat(value.TimeOfDay, dayLength);

            if (value.RepeatEveryDays <= 0)
            {
                EvaluateDay(firstDay);
                return;
            }

            int interval = Mathf.Max(1, value.RepeatEveryDays);
            int firstCandidate = firstDay;
            if (startDay > firstDay)
            {
                int intervalsPassed = Mathf.CeilToInt((startDay - firstDay) / (float)interval);
                firstCandidate = firstDay + intervalsPassed * interval;
            }

            int processed = 0;
            for (int day = firstCandidate;
                 day <= endDay && processed < MaximumOccurrencesPerAdvance;
                 day += interval, processed++)
            {
                EvaluateDay(day);
            }

            void EvaluateDay(int dayNumber)
            {
                if (dayNumber < firstDay || dayNumber <= progress.LastEvaluatedDayNumber)
                    return;

                float triggerTotalTime = (dayNumber - 1) * dayLength + triggerTimeInDay;
                if (triggerTotalTime > context.NewTotalTime + 0.0001f)
                    return;

                // Loading an existing world after a past trigger must not replay that event.
                progress.LastEvaluatedDayNumber = dayNumber;
                if (triggerTotalTime <= context.OldTotalTime + 0.0001f)
                    return;

                float chance = Mathf.Clamp01(value.Chance);
                if (chance <= 0f ||
                    (chance < 1f && DeterministicUnit(context.WorldSeed, definition.Id, dayNumber) >= chance))
                {
                    return;
                }

                results.Add(new GameEventOccurrence(triggerTotalTime, dayNumber, Type));
            }
        }

        private static DayScheduleGameEventTriggerParameters Read(JObject parameters)
        {
            return parameters?.ToObject<DayScheduleGameEventTriggerParameters>()
                   ?? new DayScheduleGameEventTriggerParameters();
        }

        internal static float DeterministicUnit(int worldSeed, string eventId, int dayNumber)
        {
            unchecked
            {
                uint hash = (uint)(worldSeed == 0 ? 1 : worldSeed);
                string stableId = eventId ?? string.Empty;
                for (int i = 0; i < stableId.Length; i++)
                    hash = (hash ^ stableId[i]) * 16777619u;

                hash = (hash ^ (uint)dayNumber) * 16777619u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777216f;
            }
        }
    }

    public sealed class ManualGameEventTrigger : IGameEventTriggerHandler
    {
        public string Type => "manual";

        public bool Validate(JObject parameters, out string error)
        {
            error = string.Empty;
            return true;
        }

        public void CollectOccurrences(
            GameEventTriggerContext context,
            GameEventDefinition definition,
            JObject parameters,
            GameEventProgressSaveData progress,
            List<GameEventOccurrence> results)
        {
            // Manual events are started through GameEventManager.TryTriggerNow.
        }
    }

    [Serializable]
    public sealed class GroundItemDwellGameEventTriggerParameters
    {
        [JsonProperty("itemId")]
        public string ItemId;

        [JsonProperty("dwellGameSeconds")]
        public float DwellGameSeconds = 120f;

        [JsonProperty("requirePickupable")]
        public bool RequirePickupable = true;
    }

    [Serializable]
    internal sealed class GroundItemDwellTriggerRuntimeState
    {
        public bool HasCandidate;
        public int CandidateItemGuid;
        public float CandidateFirstSeenTotalTime = -1f;
        public string CandidateWorldKey = string.Empty;
        public int ObservedTriggerCount;
    }

    /// <summary>
    /// 当指定世界物品持续留在地面达到时长后触发。候选 GUID 与首次发现时间会进入存档，
    /// 拾起、销毁、切换世界都会中断计时。
    /// </summary>
    public sealed class GroundItemDwellGameEventTrigger : IGameEventTriggerHandler
    {
        public string Type => "world.item.dwell";

        public bool Validate(JObject parameters, out string error)
        {
            GroundItemDwellGameEventTriggerParameters value = Read(parameters);
            if (string.IsNullOrWhiteSpace(value.ItemId))
            {
                error = "itemId cannot be empty.";
                return false;
            }

            if (value.DwellGameSeconds <= 0f)
            {
                error = "dwellGameSeconds must be greater than 0.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void CollectOccurrences(
            GameEventTriggerContext context,
            GameEventDefinition definition,
            JObject parameters,
            GameEventProgressSaveData progress,
            List<GameEventOccurrence> results)
        {
            if (context == null || definition == null || progress == null || results == null)
                return;

            GroundItemDwellGameEventTriggerParameters value = Read(parameters);
            GroundItemDwellTriggerRuntimeState runtime = ReadRuntime(progress);
            if (runtime.ObservedTriggerCount != progress.TriggerCount ||
                !string.Equals(runtime.CandidateWorldKey, context.ActiveWorldKey, StringComparison.Ordinal))
            {
                ResetCandidate(runtime);
                runtime.ObservedTriggerCount = progress.TriggerCount;
                runtime.CandidateWorldKey = context.ActiveWorldKey;
            }

            Item candidate = FindEligibleGroundItem(
                value,
                runtime.HasCandidate ? runtime.CandidateItemGuid : 0);
            if (candidate == null)
            {
                ResetCandidate(runtime);
                runtime.CandidateWorldKey = context.ActiveWorldKey;
                WriteRuntime(progress, runtime);
                return;
            }

            int candidateGuid = candidate.itemData.Guid;
            if (!runtime.HasCandidate || runtime.CandidateItemGuid != candidateGuid)
            {
                runtime.HasCandidate = true;
                runtime.CandidateItemGuid = candidateGuid;
                runtime.CandidateFirstSeenTotalTime = context.NewTotalTime;
                runtime.CandidateWorldKey = context.ActiveWorldKey;
                WriteRuntime(progress, runtime);
                return;
            }

            float dwellTime = Mathf.Max(0.01f, value.DwellGameSeconds);
            if (runtime.CandidateFirstSeenTotalTime < 0f ||
                context.NewTotalTime - runtime.CandidateFirstSeenTotalTime < dwellTime - 0.0001f ||
                IsInCooldown(definition, progress, context))
            {
                WriteRuntime(progress, runtime);
                return;
            }

            Vector3 position = candidate.transform.position;
            JObject payload = new()
            {
                ["targetItemGuid"] = candidateGuid,
                ["targetItemId"] = candidate.itemData.IDName,
                ["targetPosition"] = new JObject
                {
                    ["x"] = position.x,
                    ["y"] = position.y,
                    ["z"] = position.z
                }
            };
            int dayNumber = Mathf.FloorToInt(
                context.NewTotalTime / Mathf.Max(1f, context.DayLength)) + 1;
            results.Add(new GameEventOccurrence(
                context.NewTotalTime,
                dayNumber,
                Type,
                payload.ToString(Formatting.None)));
            WriteRuntime(progress, runtime);
        }

        private static Item FindEligibleGroundItem(
            GroundItemDwellGameEventTriggerParameters parameters,
            int preferredGuid)
        {
            ItemMgr itemManager = ItemMgr.Instance;
            if (itemManager == null)
                return null;

            List<Item> candidates = itemManager.GetItemsByNameID(parameters.ItemId.Trim());
            Item selected = null;
            int selectedGuid = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Item candidate = candidates[i];
                if (!IsEligibleGroundItem(candidate, parameters.RequirePickupable))
                    continue;

                int guid = candidate.itemData.Guid;
                if (guid == preferredGuid)
                    return candidate;

                if (selected == null || guid < selectedGuid)
                {
                    selected = candidate;
                    selectedGuid = guid;
                }
            }

            return selected;
        }

        private static bool IsEligibleGroundItem(Item candidate, bool requirePickupable)
        {
            return candidate != null &&
                   candidate.gameObject.activeInHierarchy &&
                   candidate.itemData?.Stack != null &&
                   candidate.itemData.Stack.Amount > 0f &&
                   !candidate.InHand &&
                   (!requirePickupable || candidate.itemData.Stack.CanBePickedUp);
        }

        private static bool IsInCooldown(
            GameEventDefinition definition,
            GameEventProgressSaveData progress,
            GameEventTriggerContext context)
        {
            return definition.CooldownDays > 0f &&
                   progress.LastTriggeredTotalTime >= 0f &&
                   context.NewTotalTime - progress.LastTriggeredTotalTime <
                   definition.CooldownDays * Mathf.Max(1f, context.DayLength);
        }

        private static GroundItemDwellGameEventTriggerParameters Read(JObject parameters)
        {
            return parameters?.ToObject<GroundItemDwellGameEventTriggerParameters>()
                   ?? new GroundItemDwellGameEventTriggerParameters();
        }

        private static GroundItemDwellTriggerRuntimeState ReadRuntime(
            GameEventProgressSaveData progress)
        {
            if (string.IsNullOrWhiteSpace(progress?.TriggerRuntimeDataJson))
                return new GroundItemDwellTriggerRuntimeState();

            try
            {
                return JsonConvert.DeserializeObject<GroundItemDwellTriggerRuntimeState>(
                           progress.TriggerRuntimeDataJson)
                       ?? new GroundItemDwellTriggerRuntimeState();
            }
            catch
            {
                return new GroundItemDwellTriggerRuntimeState();
            }
        }

        private static void WriteRuntime(
            GameEventProgressSaveData progress,
            GroundItemDwellTriggerRuntimeState runtime)
        {
            progress.TriggerRuntimeDataJson = JsonConvert.SerializeObject(runtime, Formatting.None);
        }

        private static void ResetCandidate(GroundItemDwellTriggerRuntimeState runtime)
        {
            runtime.HasCandidate = false;
            runtime.CandidateItemGuid = 0;
            runtime.CandidateFirstSeenTotalTime = -1f;
        }
    }

    [Serializable]
    public sealed class DimensionGameEventConditionParameters
    {
        [JsonProperty("allowedDimensionIds")]
        public List<string> AllowedDimensionIds = new();

        [JsonProperty("excludedDimensionIds")]
        public List<string> ExcludedDimensionIds = new();
    }

    public sealed class DimensionGameEventCondition : IGameEventConditionEvaluator
    {
        public string Type => "dimension.is";

        public bool Validate(JObject parameters, out string error)
        {
            DimensionGameEventConditionParameters value = Read(parameters);
            if ((value.AllowedDimensionIds == null || value.AllowedDimensionIds.Count == 0) &&
                (value.ExcludedDimensionIds == null || value.ExcludedDimensionIds.Count == 0))
            {
                error = "At least one allowedDimensionIds or excludedDimensionIds value is required.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool Evaluate(
            GameEventEvaluationContext context,
            JObject parameters,
            out string failureReason)
        {
            DimensionGameEventConditionParameters value = Read(parameters);
            string activeDimension = DimensionManager.Instance != null &&
                                     DimensionManager.Instance.ActiveAddress.IsValid
                ? DimensionManager.Instance.ActiveAddress.DimensionId
                : WorldAddress.FromWorldKey(context?.ActiveWorldKey).DimensionId;

            if (Contains(value.ExcludedDimensionIds, activeDimension))
            {
                failureReason = $"dimension '{activeDimension}' is excluded";
                return false;
            }

            if (value.AllowedDimensionIds != null &&
                value.AllowedDimensionIds.Count > 0 &&
                !Contains(value.AllowedDimensionIds, activeDimension))
            {
                failureReason = $"dimension '{activeDimension}' is not allowed";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private static DimensionGameEventConditionParameters Read(JObject parameters)
        {
            return parameters?.ToObject<DimensionGameEventConditionParameters>()
                   ?? new DimensionGameEventConditionParameters();
        }

        private static bool Contains(List<string> values, string target)
        {
            if (values == null)
                return false;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i]?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
