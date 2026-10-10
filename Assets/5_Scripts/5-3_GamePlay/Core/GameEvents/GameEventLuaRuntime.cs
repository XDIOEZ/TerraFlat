using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XLua;

namespace FlatWorld.Gameplay.Events
{
    internal sealed class GameEventLuaRuntime : IDisposable
    {
        #region 模块加载与资源归属

        public const string ResourcePath = "Config/GameEvents/Lua";
        private readonly LuaEnv environment;
        private readonly LuaFunction createEnvironment;
        private readonly Dictionary<string, LuaTable> modules = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LuaFunction> callbacks = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<JObject, Binding> bindings = new();

        private sealed class Binding
        {
            public string Script;
            public string Handler;
            public JObject Args;
        }

        public GameEventLuaRuntime()
        {
            environment = new LuaEnv();
            try
            {
                // 每份脚本只取得纯 Lua 工具，不暴露 C# 管理器或文件加载入口。
                createEnvironment = (LuaFunction)environment.DoString(@"
                    local function copy(source)
                        local result = {}; for k,v in pairs(source or {}) do result[k]=v end; return result
                    end
                    return function()
                        local env = {assert=assert, error=error, ipairs=ipairs, pairs=pairs, next=next,
                            pcall=pcall, xpcall=xpcall, select=select, tonumber=tonumber, tostring=tostring,
                            type=type, math=copy(math), string=copy(string), table=copy(table), utf8=copy(utf8)}
                        env.event = {array=function(t) t=t or {}; t.__eventArray=true; return t end}
                        env._G=env; return env
                    end", "GameEventLuaBootstrap")[0];
            }
            catch { environment.Dispose(); throw; }
        }

        public void LoadCatalogs(List<GameEventConfigSource> sources, List<GameEventConfigIssue> issues)
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcePath);
            Array.Sort(assets, (left, right) => string.CompareOrdinal(left.name, right.name));
            foreach (TextAsset asset in assets)
            {
                string script = asset.name.EndsWith(".lua", StringComparison.Ordinal)
                    ? asset.name[..^4] : asset.name;
                string sourceName = $"lua/{script}.lua.txt";
                LuaTable module = null;
                try
                {
                    if (modules.ContainsKey(script)) throw new InvalidOperationException($"重复 Lua 脚本名 '{script}'。");
                    if (asset.bytes.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Lua 事件文件超过 2MB。");
                    using LuaTable scope = (LuaTable)createEnvironment.Call()[0];
                    object[] result = environment.DoString(asset.text, $"@{sourceName}", scope);
                    module = result.Length == 1 ? result[0] as LuaTable : null;
                    if (module == null)
                    {
                        foreach (object value in result) (value as LuaBase)?.Dispose();
                        throw new InvalidOperationException("Lua 事件脚本必须只返回一个模块表。");
                    }
                    using LuaTable catalog = module.Get<LuaTable>("catalog");
                    if (catalog != null)
                        sources.Add(new GameEventConfigSource(sourceName,
                            GameEventLuaData.Read(catalog).ToString(Formatting.None)));
                    modules.Add(script, module);
                    module = null;
                }
                catch (Exception exception)
                {
                    issues.Add(new GameEventConfigIssue(sourceName, "<file>", exception.Message));
                }
                finally { module?.Dispose(); }
            }
        }

        public void Tick() => environment.Tick();

        public void Dispose()
        {
            foreach (LuaFunction function in callbacks.Values) function?.Dispose();
            callbacks.Clear();
            foreach (LuaTable module in modules.Values) module.Dispose();
            modules.Clear();
            createEnvironment.Dispose();
            environment.Dispose();
        }

        #endregion

        #region 扩展绑定与回调

        private Binding GetBinding(JObject parameters)
        {
            if (parameters == null) throw new InvalidOperationException("Lua 事件参数不能为空。");
            return bindings.GetValue(parameters, value =>
            {
                string script = value.Value<string>("script")?.Trim();
                string handler = value.Value<string>("handler")?.Trim();
                if (string.IsNullOrEmpty(script) || string.IsNullOrEmpty(handler))
                    throw new InvalidOperationException("Lua 事件需要 script 与 handler。");
                if (value["args"] != null && value["args"] is not JObject)
                    throw new InvalidOperationException("Lua 事件 args 必须是对象。");
                return new Binding { Script = script, Handler = handler, Args = (JObject)value["args"] ?? new JObject() };
            });
        }

        private LuaFunction GetCallback(Binding binding, string category, string name, bool required)
        {
            string key = $"{binding.Script}\0{category}\0{binding.Handler}\0{name}";
            if (!callbacks.TryGetValue(key, out LuaFunction function))
            {
                if (!modules.TryGetValue(binding.Script, out LuaTable module))
                    throw new InvalidOperationException($"找不到 Lua 事件脚本 '{binding.Script}'。");
                using LuaTable group = module.Get<LuaTable>(category);
                using LuaTable handler = group?.Get<LuaTable>(binding.Handler);
                if (handler == null)
                    throw new InvalidOperationException($"找不到 {binding.Script}/{category}/{binding.Handler}。");
                function = handler.Get<LuaFunction>(name);
                callbacks.Add(key, function);
            }
            if (required && function == null)
                throw new InvalidOperationException($"Lua 事件 {binding.Script}/{binding.Handler} 缺少 {name} 回调。");
            return function;
        }

        public bool Validate(JObject parameters, string category, string requiredCallback, out string error)
        {
            try
            {
                bindings.Remove(parameters);
                Binding binding = GetBinding(parameters);
                GetCallback(binding, category, requiredCallback, true);
                if (category == "actions") GetCallback(binding, category, "tick", true);
                LuaFunction validate = GetCallback(binding, category, "validate", false);
                if (validate != null)
                {
                    using LuaTable args = CreateTable(binding.Args);
                    JToken[] result = ReadResults(validate.Call(args));
                    if (result.Length == 0 || result[0].Type != JTokenType.Boolean || !result[0].Value<bool>())
                        throw new InvalidOperationException(result.Length > 1 ? result[1].ToString() : "Lua 参数校验未返回 true。");
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public JToken[] Invoke(JObject parameters, string category, string callback,
            GameEventLuaContext context, LuaTable state = null, bool cancelled = false, bool required = false)
        {
            Binding binding = GetBinding(parameters);
            LuaFunction function = GetCallback(binding, category, callback, required);
            if (function == null) return Array.Empty<JToken>();
            using LuaTable args = CreateTable(binding.Args);
            return ReadResults(function.Call(context, args, state, cancelled));
        }

        private static JToken[] ReadResults(object[] values)
        {
            try
            {
                JToken[] result = new JToken[values.Length];
                for (int i = 0; i < values.Length; i++) result[i] = GameEventLuaData.Read(values[i]);
                return result;
            }
            finally { foreach (object value in values) (value as LuaBase)?.Dispose(); }
        }

        public LuaTable CreateTable(JToken token) => GameEventLuaData.CreateTable(environment, token);

        #endregion
    }
}
