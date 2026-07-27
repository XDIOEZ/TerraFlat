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
        mainTable = result != null && result.Length > 0 ? result[0] as LuaTable : null;
        mainTable ??= mainEnvironment;
        InvokeMain("OnLoad", api);
    }

    #endregion

    #region 生命周期

    public string CaptureGlobalState(string currentState)
    {
        object[] result = InvokeMain("OnSave", api, currentState ?? string.Empty);
        return GetReturnedString(result, currentState);
    }

    public void RestoreGlobalState(string state)
    {
        InvokeMain("OnLoadSave", api, state ?? string.Empty);
    }

    public string InvokeModule(string relativePath, string functionName, Item item, string state, float deltaTime = 0f)
    {
        LuaTable moduleTable = GetModuleTable(relativePath);
        LuaFunction function = moduleTable?.Get<LuaFunction>(functionName);
        if (function == null)
            return state;

        try
        {
            object[] result = function.Call(new ModItemApi(item), state ?? string.Empty, deltaTime, api);
            return GetReturnedString(result, state);
        }
        finally
        {
            function.Dispose();
        }
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
        try
        {
            InvokeMain("OnUnload", api);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MOD:{modId}] OnUnload 执行失败：{ex.Message}");
        }

        foreach (LuaTable table in moduleTables.Values)
            table?.Dispose();

        moduleTables.Clear();
        if (mainTable != null && !ReferenceEquals(mainTable, mainEnvironment))
            mainTable.Dispose();
        mainEnvironment?.Dispose();
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
        LuaFunction function = mainTable?.Get<LuaFunction>(functionName);
        if (function == null)
            return null;

        try
        {
            return function.Call(args);
        }
        finally
        {
            function.Dispose();
        }
    }

    private static string GetReturnedString(object[] result, string fallback)
    {
        return result != null && result.Length > 0 && result[0] is string value
            ? value
            : fallback ?? string.Empty;
    }

    #endregion
}
