using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XLua;

namespace FlatWorld.Gameplay.Events
{
    #region Lua 行动存档数据

    internal sealed class GameEventLuaActionData
    {
        public JObject ScriptState = new();
        public Dictionary<string, GameEventLuaChildActionData> Children = new(StringComparer.Ordinal);
    }

    internal sealed class GameEventLuaChildActionData
    {
        public GameEventActionDefinition Definition;
        public GameEventActionRuntimeSaveData State;
        public bool Ended;
    }

    #endregion

    public sealed class GameEventLuaContext : IDisposable
    {
        #region 回调上下文与只读查询

        private GameEventLuaRuntime runtime;
        private GameEventActionContext actionContext;
        private GameEventLuaActionData actionData;
        private readonly List<LuaTable> returnedTables = new();
        private readonly bool canStartActions;
        private bool closed;
        private readonly JObject payload;

        public string EventId { get; }
        public string WorldKey { get; }
        public float Now { get; }
        public float OldTime { get; }
        public float DayLength { get; }
        public float StartedAt { get; }
        public bool IsGmForced { get; }
        public int WorldSeed { get; }
        public int TriggerCount { get; }
        public float LastTriggeredAt { get; }

        internal GameEventLuaContext(GameEventLuaRuntime runtime, string eventId, string worldKey,
            float now, float dayLength, JObject payload = null, float oldTime = 0f,
            int worldSeed = 0, GameEventProgressSaveData progress = null,
            GameEventActionContext action = null, GameEventLuaActionData data = null, bool canStart = false)
        {
            this.runtime = runtime;
            EventId = eventId;
            WorldKey = worldKey;
            Now = now;
            OldTime = oldTime;
            DayLength = dayLength;
            this.payload = payload ?? new JObject();
            WorldSeed = worldSeed;
            TriggerCount = progress?.TriggerCount ?? 0;
            LastTriggeredAt = progress?.LastTriggeredTotalTime ?? -1f;
            StartedAt = action?.ActiveEvent.StartedTotalTime ?? now;
            IsGmForced = action?.IsGmForced ?? false;
            actionContext = action;
            actionData = data;
            canStartActions = canStart;
        }

        public LuaTable GetPlayers()
        {
            EnsureOpen();
            JArray players = new();
            ItemMgr manager = ItemMgr.Instance;
            if (manager != null)
            {
                List<Player> sorted = new();
                foreach (Player player in manager.Player_DIC.Values)
                {
                    if (player == null || !player.gameObject.activeInHierarchy ||
                        (player.Data != null && !string.IsNullOrEmpty(player.Data.CurrentSceneName) &&
                         !string.Equals(player.Data.CurrentSceneName, WorldKey, StringComparison.Ordinal))) continue;
                    sorted.Add(player);
                }
                sorted.Sort((left, right) => string.CompareOrdinal(left.ProfileName, right.ProfileName));
                foreach (Player player in sorted)
                {
                    Vector3 position = player.transform.position;
                    players.Add(new JObject { ["id"] = player.ProfileName, ["x"] = position.x, ["y"] = position.y });
                }
            }
            return KeepTable(players);
        }

        public LuaTable GetPayload() => KeepTable(payload);

        public float Distance(float x1, float y1, float x2, float y2)
        {
            EnsureOpen();
            return Mathf.Sqrt(WorldTopologyRuntime.SqrDistance(new Vector3(x1, y1, 0f), new Vector3(x2, y2, 0f)));
        }

        public void Log(string message)
        {
            EnsureOpen();
            Debug.Log($"[GameEvent Lua:{EventId}] {message}");
        }

        private LuaTable KeepTable(JToken data)
        {
            EnsureOpen();
            LuaTable table = runtime.CreateTable(data);
            returnedTables.Add(table);
            return table;
        }

        private void EnsureOpen()
        {
            if (closed) throw new InvalidOperationException("Lua 事件上下文只能在当前回调中使用。");
        }

        #endregion

        #region 原生行动组合与生命周期

        // 子行动的身份和参数首次确定后保持不变，进度随父行动保存。
        public bool RunAction(string id, string type, LuaTable parameters)
        {
            EnsureMutation();
            if (!canStartActions) throw new InvalidOperationException("只有 begin/tick 回调可以启动或推进行动。");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
                throw new InvalidOperationException("子行动 id 必须是非空且不超过 128 字符的稳定键。");
            type = GameEventConfigLoader.NormalizeType(type);
            if (type == "lua") throw new InvalidOperationException("Lua 子行动不能递归启动 Lua 行动。");
            JObject arguments = GameEventLuaData.Read(parameters) as JObject
                ?? throw new InvalidOperationException("子行动参数必须是对象。");
            if (!GameEventExtensionRegistry.TryGetAction(type, out IGameEventActionHandler handler))
                throw new InvalidOperationException($"未知子行动类型 '{type}'。");

            if (!actionData.Children.TryGetValue(id, out GameEventLuaChildActionData child))
            {
                if (actionData.Children.Count >= 128) throw new InvalidOperationException("单个 Lua 行动最多持有 128 个子行动。");
                if (!handler.Validate(arguments, out string error)) throw new InvalidOperationException(error);
                child = new GameEventLuaChildActionData
                {
                    Definition = new GameEventActionDefinition { Id = id, Type = type, Parameters = arguments },
                    State = new GameEventActionRuntimeSaveData { ActionId = id }
                };
                actionData.Children.Add(id, child);
            }
            else if (child.Definition.Type != type || !JToken.DeepEquals(child.Definition.Parameters, arguments))
                throw new InvalidOperationException($"子行动 '{id}' 不能变更类型或参数；请使用新的 id。");
            if (child.Ended) throw new InvalidOperationException($"子行动 '{id}' 已停止；请使用新的 id。");
            if (child.State.Completed) return true;
            GameEventActionContext context = CreateChildContext(child);
            bool started = child.State.Started;
            child.State.Started = true;
            child.State.Completed = (started
                ? handler.Tick(context, child.Definition.Parameters, child.State)
                : handler.Begin(context, child.Definition.Parameters, child.State)) == GameEventActionStatus.Completed;
            return child.State.Completed;
        }

        public void StopAction(string id)
        {
            EnsureMutation();
            if (actionData.Children.TryGetValue(id, out GameEventLuaChildActionData child) && !child.Ended)
                EndChild(child, true, false);
        }

        internal void ResumeChildren()
        {
            EnsureMutation();
            foreach (GameEventLuaChildActionData child in actionData.Children.Values)
            {
                if (child.Ended || !child.State.Started) continue;
                if (!GameEventExtensionRegistry.TryGetAction(child.Definition.Type, out IGameEventActionHandler handler) ||
                    !handler.Validate(child.Definition.Parameters, out string error))
                    throw new InvalidOperationException($"不能恢复 Lua 子行动 '{child.Definition.Id}'。");
                handler.Resume(CreateChildContext(child), child.Definition.Parameters, child.State);
            }
        }

        internal void EndChildren(bool cancelled, bool suspend = false)
        {
            EnsureMutation();
            foreach (GameEventLuaChildActionData child in actionData.Children.Values)
            {
                if (child.Ended || !child.State.Started) continue;
                try { EndChild(child, cancelled, suspend); }
                catch (Exception exception) { Debug.LogError($"[GameEvent Lua:{EventId}] 子行动清理失败：{exception}"); }
            }
        }

        private void EndChild(GameEventLuaChildActionData child, bool cancelled, bool suspend)
        {
            try
            {
                if (GameEventExtensionRegistry.TryGetAction(child.Definition.Type, out IGameEventActionHandler handler))
                {
                    if (suspend)
                    {
                        if (handler is IGameEventSuspendableAction suspendable)
                            suspendable.Suspend(CreateChildContext(child), child.Definition.Parameters, child.State);
                    }
                    else handler.End(CreateChildContext(child), child.Definition.Parameters, child.State, cancelled);
                }
            }
            finally { if (!suspend) child.Ended = true; }
        }

        private GameEventActionContext CreateChildContext(GameEventLuaChildActionData child)
        {
            GameEventDefinition parent = actionContext.Definition;
            GameEventDefinition owner = new()
            {
                Id = $"lua:{parent.Id.Length}:{parent.Id}:{actionContext.Action.Id.Length}:{actionContext.Action.Id}:{child.Definition.Id}",
                DisplayName = parent.DisplayName,
                DurationDays = parent.DurationDays,
                SourceName = parent.SourceName
            };
            return new GameEventActionContext(actionContext.Manager, owner, child.Definition,
                actionContext.ActiveEvent, WorldKey, Now, DayLength);
        }

        private void EnsureMutation()
        {
            EnsureOpen();
            if (!GameNetwork.HasStateAuthority || actionContext == null || actionData == null)
                throw new InvalidOperationException("只有权威端的事件行动回调可以修改游戏状态。");
        }

        void IDisposable.Dispose()
        {
            if (closed) return;
            closed = true;
            foreach (LuaTable table in returnedTables) table.Dispose();
            returnedTables.Clear();
            actionContext = null;
            actionData = null;
            runtime = null;
        }

        #endregion
    }
}
