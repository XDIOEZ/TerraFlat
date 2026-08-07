using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>
    /// String-keyed extension registry used by JSON. New code can register another
    /// trigger, condition or action without changing the catalog data model.
    /// </summary>
    public static class GameEventExtensionRegistry
    {
        private static readonly Dictionary<string, IGameEventTriggerHandler> TriggerHandlers =
            new(StringComparer.Ordinal);
        private static readonly Dictionary<string, IGameEventConditionEvaluator> ConditionEvaluators =
            new(StringComparer.Ordinal);
        private static readonly Dictionary<string, IGameEventActionHandler> ActionHandlers =
            new(StringComparer.Ordinal);

        private static bool builtInsRegistered;

        public static void EnsureBuiltInsRegistered()
        {
            if (builtInsRegistered)
                return;

            builtInsRegistered = true;
            RegisterTrigger(new DayScheduleGameEventTrigger());
            RegisterTrigger(new ManualGameEventTrigger());
            RegisterTrigger(new GroundItemDwellGameEventTrigger());
            RegisterCondition(new DimensionGameEventCondition());
            RegisterAction(new CreatureWavesGameEventAction());
            RegisterAction(new CreatureAdvanceGameEventAction());
            RegisterAction(new WeatherOverrideGameEventAction());
            RegisterAction(new EmitSignalGameEventAction());
        }

        public static bool RegisterTrigger(IGameEventTriggerHandler handler, bool replace = false)
        {
            return Register(TriggerHandlers, handler?.Type, handler, replace);
        }

        public static bool RegisterCondition(IGameEventConditionEvaluator evaluator, bool replace = false)
        {
            return Register(ConditionEvaluators, evaluator?.Type, evaluator, replace);
        }

        public static bool RegisterAction(IGameEventActionHandler handler, bool replace = false)
        {
            return Register(ActionHandlers, handler?.Type, handler, replace);
        }

        public static bool TryGetTrigger(string type, out IGameEventTriggerHandler handler)
        {
            EnsureBuiltInsRegistered();
            return TriggerHandlers.TryGetValue(GameEventConfigLoader.NormalizeType(type), out handler);
        }

        public static bool TryGetCondition(string type, out IGameEventConditionEvaluator evaluator)
        {
            EnsureBuiltInsRegistered();
            return ConditionEvaluators.TryGetValue(GameEventConfigLoader.NormalizeType(type), out evaluator);
        }

        public static bool TryGetAction(string type, out IGameEventActionHandler handler)
        {
            EnsureBuiltInsRegistered();
            return ActionHandlers.TryGetValue(GameEventConfigLoader.NormalizeType(type), out handler);
        }

        private static bool Register<T>(
            Dictionary<string, T> registry,
            string rawType,
            T extension,
            bool replace)
            where T : class
        {
            string type = GameEventConfigLoader.NormalizeType(rawType);
            if (extension == null || string.IsNullOrWhiteSpace(type))
                return false;

            if (registry.ContainsKey(type) && !replace)
                return false;

            registry[type] = extension;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            TriggerHandlers.Clear();
            ConditionEvaluators.Clear();
            ActionHandlers.Clear();
            builtInsRegistered = false;
        }
    }
}
