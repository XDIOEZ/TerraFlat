using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XLua;

namespace FlatWorld.Gameplay.Events
{
    internal sealed class LuaGameEventAction : IGameEventActionHandler
    {
        #region Lua 行动生命周期

        private readonly GameEventLuaRuntime runtime;
        public string Type => "lua";
        public LuaGameEventAction(GameEventLuaRuntime runtime) => this.runtime = runtime;
        public bool Validate(JObject parameters, out string error) => runtime.Validate(parameters, "actions", "begin", out error);

        public GameEventActionStatus Begin(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state)
            => Execute(context, parameters, state, "begin");

        public GameEventActionStatus Tick(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state)
            => Execute(context, parameters, state, "tick");

        public void Resume(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state)
            => Execute(context, parameters, state, "resume");

        public void End(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state, bool cancelled)
            => Execute(context, parameters, state, "finish", cancelled);

        private GameEventActionStatus Execute(GameEventActionContext context, JObject parameters,
            GameEventActionRuntimeSaveData state, string callback, bool cancelled = false)
        {
            GameEventLuaActionData data = ReadData(state);
            using GameEventLuaContext scope = CreateContext(context, data, callback is "begin" or "tick");
            using LuaTable scriptState = runtime.CreateTable(data.ScriptState);
            try
            {
                if (callback == "resume") scope.ResumeChildren();
                JToken[] result = runtime.Invoke(parameters, "actions", callback, scope, scriptState, cancelled,
                    required: callback is "begin" or "tick");
                if (callback is "resume" or "finish") return GameEventActionStatus.Running;
                if (result.Length == 0 || result[0].Type != JTokenType.Boolean)
                    throw new InvalidOperationException("Lua 行动 begin/tick 必须返回 true（完成）或 false（继续）。");
                return result[0].Value<bool>() ? GameEventActionStatus.Completed : GameEventActionStatus.Running;
            }
            catch
            {
                scope.EndChildren(true);
                throw;
            }
            finally
            {
                if (callback == "finish") scope.EndChildren(cancelled);
                try
                {
                    data.ScriptState = GameEventLuaData.Read(scriptState) as JObject
                        ?? throw new InvalidOperationException("Lua 行动 state 必须保持为对象。");
                }
                catch { scope.EndChildren(true); throw; }
                finally { WriteData(state, data); }
            }
        }

        // 离开世界只移除临时效果，保留父子进度，返回源世界时由 Resume 恢复。
        internal void Suspend(GameEventActionContext context, GameEventActionRuntimeSaveData state)
        {
            GameEventLuaActionData data = ReadData(state);
            using GameEventLuaContext scope = CreateContext(context, data, false);
            try { scope.EndChildren(true, suspend: true); }
            finally { WriteData(state, data); }
        }

        private GameEventLuaContext CreateContext(GameEventActionContext context, GameEventLuaActionData data, bool canStart)
            => new(runtime, context.Definition.Id, context.ActiveWorldKey, context.CurrentTotalTime, context.DayLength,
                context.TriggerPayload, action: context, data: data, canStart: canStart);

        private static GameEventLuaActionData ReadData(GameEventActionRuntimeSaveData state)
        {
            GameEventLuaActionData data = string.IsNullOrEmpty(state.RuntimeDataJson) ? new()
                : JsonConvert.DeserializeObject<GameEventLuaActionData>(state.RuntimeDataJson)
                  ?? throw new InvalidOperationException("Lua 行动存档为空。");
            data.ScriptState ??= new JObject();
            data.Children ??= new Dictionary<string, GameEventLuaChildActionData>(StringComparer.Ordinal);
            foreach (GameEventLuaChildActionData child in data.Children.Values)
                if (child?.Definition == null || child.State == null || child.Definition.Type == "lua")
                    throw new InvalidOperationException("Lua 子行动存档不完整或包含递归行动。");
            return data;
        }

        private static void WriteData(GameEventActionRuntimeSaveData state, GameEventLuaActionData data)
            => state.RuntimeDataJson = JsonConvert.SerializeObject(data, Formatting.None);

        #endregion
    }

    internal sealed class LuaGameEventCondition : IGameEventConditionEvaluator
    {
        #region Lua 条件

        private readonly GameEventLuaRuntime runtime;
        public string Type => "lua";
        public LuaGameEventCondition(GameEventLuaRuntime runtime) => this.runtime = runtime;
        public bool Validate(JObject parameters, out string error) => runtime.Validate(parameters, "conditions", "evaluate", out error);

        public bool Evaluate(GameEventEvaluationContext context, JObject parameters, out string failureReason)
        {
            try
            {
                using GameEventLuaContext scope = new(runtime, context.Definition.Id, context.ActiveWorldKey,
                    context.CurrentTotalTime, context.DayLength, context.TriggerPayload);
                JToken[] result = runtime.Invoke(parameters, "conditions", "evaluate", scope, required: true);
                if (result.Length == 0 || result[0].Type != JTokenType.Boolean)
                    throw new InvalidOperationException("Lua 条件 evaluate 必须返回布尔值。");
                failureReason = result.Length > 1 ? result[1].ToString() : string.Empty;
                return result[0].Value<bool>();
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                Debug.LogError($"[GameEvent Lua:{context.Definition.Id}] 条件执行失败：{exception}");
                return false;
            }
        }

        #endregion
    }

    internal sealed class LuaGameEventTrigger : IGameEventTriggerHandler
    {
        #region Lua 触发器

        private readonly GameEventLuaRuntime runtime;
        public string Type => "lua";
        public LuaGameEventTrigger(GameEventLuaRuntime runtime) => this.runtime = runtime;
        public bool Validate(JObject parameters, out string error) => runtime.Validate(parameters, "triggers", "collect", out error);

        public void CollectOccurrences(GameEventTriggerContext context, GameEventDefinition definition, JObject parameters,
            GameEventProgressSaveData progress, List<GameEventOccurrence> results)
        {
            JObject saved = string.IsNullOrEmpty(progress.TriggerRuntimeDataJson) ? new JObject()
                : JObject.Parse(progress.TriggerRuntimeDataJson);
            using LuaTable state = runtime.CreateTable(saved);
            using GameEventLuaContext scope = new(runtime, definition.Id, context.ActiveWorldKey,
                context.NewTotalTime, context.DayLength, oldTime: context.OldTotalTime,
                worldSeed: context.WorldSeed, progress: progress);
            List<GameEventOccurrence> candidates = new();
            try
            {
                JToken[] returned = runtime.Invoke(parameters, "triggers", "collect", scope, state, required: true);
                if (returned.Length == 0 || returned[0].Type == JTokenType.Null) return;
                if (returned[0] is not JArray occurrences || occurrences.Count > 256)
                    throw new InvalidOperationException("Lua collect 必须返回最多 256 个候选的连续数组或 nil。");
                foreach (JToken entry in occurrences)
                {
                    if (entry is not JObject occurrence) throw new InvalidOperationException("Lua 触发候选必须是对象。");
                    float time = occurrence.Value<float?>("time") ?? context.NewTotalTime;
                    if (float.IsNaN(time) || float.IsInfinity(time) || time < context.OldTotalTime || time > context.NewTotalTime)
                        throw new InvalidOperationException("Lua 触发时间必须在本次时间推进区间内。");
                    JObject payload = occurrence["payload"] as JObject ?? new JObject();
                    candidates.Add(new GameEventOccurrence(time, Mathf.FloorToInt(time / context.DayLength) + 1,
                        occurrence.Value<string>("cause") ?? "lua", payload.ToString(Formatting.None)));
                }
            }
            finally
            {
                JObject current = GameEventLuaData.Read(state) as JObject
                    ?? throw new InvalidOperationException("Lua 触发器 state 必须保持为对象。");
                if (!JToken.DeepEquals(saved, current)) progress.TriggerRuntimeDataJson = current.ToString(Formatting.None);
            }
            results.AddRange(candidates);
        }

        #endregion
    }
}
