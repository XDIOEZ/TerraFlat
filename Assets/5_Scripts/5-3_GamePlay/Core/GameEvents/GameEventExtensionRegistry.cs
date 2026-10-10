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
        #region 扩展注册与查询

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

        // Lua 适配器与管理器资源会话一起替换，注册表不能持有已释放的虚拟机。
        internal static void SetLuaRuntime(GameEventLuaRuntime runtime)
        {
            if (runtime == null)
            {
                if (TriggerHandlers.GetValueOrDefault("lua") is LuaGameEventTrigger) TriggerHandlers.Remove("lua");
                if (ConditionEvaluators.GetValueOrDefault("lua") is LuaGameEventCondition) ConditionEvaluators.Remove("lua");
                if (ActionHandlers.GetValueOrDefault("lua") is LuaGameEventAction) ActionHandlers.Remove("lua");
                return;
            }
            RegisterTrigger(new LuaGameEventTrigger(runtime), replace: true);
            RegisterCondition(new LuaGameEventCondition(runtime), replace: true);
            RegisterAction(new LuaGameEventAction(runtime), replace: true);
        }

        public static bool TryGetTrigger(string type, out IGameEventTriggerHandler handler)
        {
            EnsureBuiltInsRegistered();
            return TryGet(TriggerHandlers, type, out handler);
        }

        public static bool TryGetCondition(string type, out IGameEventConditionEvaluator evaluator)
        {
            EnsureBuiltInsRegistered();
            return TryGet(ConditionEvaluators, type, out evaluator);
        }

        public static bool TryGetAction(string type, out IGameEventActionHandler handler)
        {
            EnsureBuiltInsRegistered();
            return TryGet(ActionHandlers, type, out handler);
        }

        private static bool TryGet<T>(Dictionary<string, T> registry, string type, out T extension)
        {
            // 已校验的类型直接查表，外部原始输入仍走统一规范化。
            if (type != null && registry.TryGetValue(type, out extension))
                return true;

            return registry.TryGetValue(GameEventConfigLoader.NormalizeType(type), out extension);
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

        #endregion
    }
}
