using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using XLua;

internal sealed class ModLuaRuntime : IDisposable
{
    private const int MaximumLuaBytes = 2 * 1024 * 1024;
    #region 字段

    private readonly string modId;
    private readonly string packageRoot;
    private readonly ModApi api;
    private readonly LuaEnv luaEnv;
    private readonly LuaTable mainEnvironment;
    private readonly Dictionary<string, LuaTable> moduleTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LuaFunction> mainCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, LuaFunction>> moduleCallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly LuaFunction bindCallback;
    private LuaTable mainTable;

    #endregion

    #region 初始化

    public ModLuaRuntime(string modId, string packageRoot, ModApi api)
    {
        this.modId = modId;
        this.packageRoot = packageRoot;
        this.api = api;
        luaEnv = new LuaEnv();

        // 首期定位为可信本地 MOD，但仍移除默认的高风险入口。
        luaEnv.DoString("CS=nil; io=nil; os=nil; debug=nil; package=nil; require=nil; dofile=nil; loadfile=nil");
        // 每种回调只创建一次跨语言句柄，调用时仍读取表中的最新函数。
        bindCallback = (LuaFunction)luaEnv.DoString(@"
return function(target, name)
    return function(...)
        local callback = target[name]
        if callback == nil then return false end
        return true, callback(...)
    end
end", "ModLuaCallbackBinder")[0];
        mainEnvironment = CreateEnvironment();
    }

    public void LoadMain(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        string fullPath = ModPathUtility.ResolvePackagePath(packageRoot, relativePath, true);
        if (new FileInfo(fullPath).Length > MaximumLuaBytes)
            throw new InvalidDataException($"MOD {modId} Lua 文件超过 2MB 限制：{relativePath}");
        string source = File.ReadAllText(fullPath);
        object[] result = luaEnv.DoString(source, $"@{modId}/{relativePath.Replace('\\', '/')}", mainEnvironment);
        LuaTable nextTable = result != null && result.Length > 0 ? result[0] as LuaTable : null;
        nextTable ??= mainEnvironment;
        DisposeCallbacks(mainCallbacks);
        if (mainTable != null && !ReferenceEquals(mainTable, mainEnvironment) && !ReferenceEquals(mainTable, nextTable))
            mainTable.Dispose();
        mainTable = nextTable;
        InvokeMain("OnLoad", api);
    }

    #endregion

    #region 生命周期

    public string CaptureGlobalState(string currentState)
    {
        object[] result = InvokeMain("OnSave", api, currentState ?? string.Empty);
        return GetReturnedString(result, currentState, 1);
    }

    public void RestoreGlobalState(string state)
    {
        InvokeMain("OnLoadSave", api, state ?? string.Empty);
    }

    public string InvokeModule(string relativePath, string functionName, Item item, string state, float deltaTime = 0f)
    {
        LuaTable moduleTable = GetModuleTable(relativePath);
        string normalizedPath = relativePath.Replace('\\', '/');
        if (!moduleCallbacks.TryGetValue(normalizedPath, out Dictionary<string, LuaFunction> callbacks))
            moduleCallbacks.Add(normalizedPath, callbacks = new Dictionary<string, LuaFunction>(StringComparer.Ordinal));
        LuaFunction function = GetCallback(moduleTable, callbacks, functionName);
        object[] result = function.Call(new ModItemApi(item), state ?? string.Empty, deltaTime, api);
        return GetReturnedString(result, state, 1);
    }

    public void Tick(float deltaTime)
    {
        InvokeMain("OnUpdate", api, deltaTime);
        luaEnv.Tick();
    }

    public void InvokeEvent(string eventName, string payloadJson)
    {
        InvokeMain("OnEvent", api, eventName ?? string.Empty, payloadJson ?? "{}");

        string callbackName = eventName switch
        {
            "content.ready" => "OnContentReady",
            "world.entered" => "OnWorldEntered",
            "world.exiting" => "OnWorldExiting",
            "player.entered" => "OnPlayerEntered",
            "item.spawned" => "OnItemSpawned",
            "item.despawning" => "OnItemDespawning",
            "scene.loaded" => "OnSceneLoaded",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(callbackName))
            InvokeMain(callbackName, api, payloadJson ?? "{}");
    }

    public void Dispose()
    {
        DisposeRuntime(notifyUnload: true);
    }

    /// <summary>候选失败或旧代释放只回收 Lua，不让 OnUnload 修改仍在运行的新世界上下文。</summary>
    internal void DisposeWithoutCallbacks() => DisposeRuntime(notifyUnload: false);

    private void DisposeRuntime(bool notifyUnload)
    {
        try
        {
            if (notifyUnload) InvokeMain("OnUnload", api);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MOD:{modId}] OnUnload 执行失败：{ex.Message}");
        }

        DisposeCallbacks(mainCallbacks);
        foreach (Dictionary<string, LuaFunction> callbacks in moduleCallbacks.Values)
            DisposeCallbacks(callbacks);
        moduleCallbacks.Clear();
        foreach (LuaTable table in moduleTables.Values)
            table?.Dispose();

        moduleTables.Clear();
        if (mainTable != null && !ReferenceEquals(mainTable, mainEnvironment))
            mainTable.Dispose();
        mainEnvironment?.Dispose();
        bindCallback?.Dispose();
        luaEnv?.Dispose();
    }

    #endregion

    #region 内部方法

    private LuaTable GetModuleTable(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException($"[MOD:{modId}] Lua 模块脚本路径为空");

        string normalizedPath = relativePath.Replace('\\', '/');
        if (moduleTables.TryGetValue(normalizedPath, out LuaTable table))
            return table;

        string fullPath = ModPathUtility.ResolvePackagePath(packageRoot, normalizedPath, true);
        if (new FileInfo(fullPath).Length > MaximumLuaBytes)
            throw new InvalidDataException($"MOD {modId} Lua 文件超过 2MB 限制：{normalizedPath}");
        string source = File.ReadAllText(fullPath);
        LuaTable environment = CreateEnvironment();
        object[] result = luaEnv.DoString(source, $"@{modId}/{normalizedPath}", environment);
        table = result != null && result.Length > 0 ? result[0] as LuaTable : null;
        table ??= environment;

        if (!ReferenceEquals(table, environment))
            environment.Dispose();

        moduleTables.Add(normalizedPath, table);
        return table;
    }

    private LuaTable CreateEnvironment()
    {
        LuaTable environment = luaEnv.NewTable();
        environment.Set("mod", api);
        environment.Set("_G", environment);
        luaEnv.Global.Set("__flatworld_mod_environment", environment);
        luaEnv.DoString("setmetatable(__flatworld_mod_environment, { __index = _G }); __flatworld_mod_environment = nil");
        return environment;
    }

    private object[] InvokeMain(string functionName, params object[] args)
    {
        return mainTable == null ? null : GetCallback(mainTable, mainCallbacks, functionName).Call(args);
    }

    private LuaFunction GetCallback(LuaTable table, Dictionary<string, LuaFunction> callbacks, string name)
    {
        if (callbacks.TryGetValue(name, out LuaFunction callback))
            return callback;
        callback = (LuaFunction)bindCallback.Call(table, name)[0];
        callbacks.Add(name, callback);
        return callback;
    }

    private static void DisposeCallbacks(Dictionary<string, LuaFunction> callbacks)
    {
        foreach (LuaFunction callback in callbacks.Values)
            callback.Dispose();
        callbacks.Clear();
    }

    private static string GetReturnedString(object[] result, string fallback, int valueIndex)
    {
        return result != null && result.Length > valueIndex && result[0] is true && result[valueIndex] is string value
            ? value
            : fallback ?? string.Empty;
    }

    #endregion
}
