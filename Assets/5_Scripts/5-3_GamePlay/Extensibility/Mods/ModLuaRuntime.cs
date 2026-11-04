using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using XLua;

/// <summary>只管理当前 MOD 的身份、存档与清理；玩法直接调用 CS 中的正式系统。</summary>
public sealed class ModLuaContext
{
    #region MOD 会话
    private readonly ModRuntimeManager manager;
    private readonly List<Action> cleanup = new();
    private bool disposed;
    public string ModId { get; }
    public bool CanModifyWorld => !disposed && manager != null && manager.CanModifyWorld;

    internal ModLuaContext(ModRuntimeManager manager, string modId)
    {
        this.manager = manager;
        ModId = modId;
    }

    public void Log(string message) => Debug.Log($"[MOD:{ModId}] {message}");
    public string GetGlobalState() => manager?.GetGlobalState(ModId) ?? string.Empty;
    public void SetGlobalState(string state)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ModLuaContext));
        if (manager == null) throw new InvalidOperationException("MOD 管理器尚未就绪。");
        manager.SetGlobalState(ModId, state);
    }

    // 订阅游戏事件后登记对应的退订动作，在销毁 LuaEnv 之前逆序释放。
    public void RegisterCleanup(Action action)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ModLuaContext));
        cleanup.Add(action ?? throw new ArgumentNullException(nameof(action)));
    }

    internal void ReleaseRegistrations()
    {
        if (disposed) return;
        disposed = true;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { Debug.LogException(error); }
        cleanup.Clear();
    }
    #endregion
}

internal sealed class ModLuaRuntime : IDisposable
{
    private const int MaximumLuaBytes = 2 * 1024 * 1024;
    #region 字段

    private readonly string modId;
    private readonly string packageRoot;
    private readonly ModLuaContext context;
    private readonly ModRuntimeManager manager;
    private readonly LuaEnv luaEnv;
    private readonly LuaTable mainEnvironment;
    private readonly Dictionary<string, LuaTable> moduleTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LuaFunction> mainCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, LuaFunction>> moduleCallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly LuaFunction bindCallback;
    private LuaTable mainTable;
    private bool disposed;
    public bool HasLoadedScripts { get; private set; }

    #endregion

    #region 初始化

    public ModLuaRuntime(string modId, string packageRoot, ModRuntimeManager manager)
    {
        this.modId = modId;
        this.packageRoot = packageRoot;
        this.manager = manager;
        context = new ModLuaContext(manager, modId);
        luaEnv = new LuaEnv();

        // 外部 Lua 是受信任代码；保留 CS，只把 require 的文件来源固定在当前包。
        luaEnv.AddLoader(LoadPackageModule);
        luaEnv.Global.Set("mod", context);
        luaEnv.DoString("io=nil; os=nil; debug=nil; dofile=nil; loadfile=nil; package.path=''; package.cpath=''");
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

        string source = ReadPackageScript(relativePath);
        object[] result = luaEnv.DoString(source, $"@{modId}/{relativePath.Replace('\\', '/')}", mainEnvironment);
        LuaTable nextTable = result != null && result.Length > 0 ? result[0] as LuaTable : null;
        nextTable ??= mainEnvironment;
        DisposeCallbacks(mainCallbacks);
        if (mainTable != null && !ReferenceEquals(mainTable, mainEnvironment) && !ReferenceEquals(mainTable, nextTable))
            mainTable.Dispose();
        mainTable = nextTable;
        InvokeMain("OnLoad", context);
    }

    #endregion

    #region 生命周期

    public string CaptureGlobalState(string currentState)
    {
        object[] result = InvokeMain("OnSave", context, currentState ?? string.Empty);
        return GetReturnedString(result, currentState, 1);
    }

    public void RestoreGlobalState(string state)
    {
        InvokeMain("OnLoadSave", context, state ?? string.Empty);
    }

    public string InvokeModule(string relativePath, string functionName, Item item, string state, float deltaTime = 0f)
    {
        LuaTable moduleTable = GetModuleTable(relativePath);
        string normalizedPath = relativePath.Replace('\\', '/');
        if (!moduleCallbacks.TryGetValue(normalizedPath, out Dictionary<string, LuaFunction> callbacks))
            moduleCallbacks.Add(normalizedPath, callbacks = new Dictionary<string, LuaFunction>(StringComparer.Ordinal));
        LuaFunction function = GetCallback(moduleTable, callbacks, functionName);
        object[] result = function.Call(item, state ?? string.Empty, deltaTime, context);
        return GetReturnedString(result, state, 1);
    }

    public void Tick(float deltaTime)
    {
        InvokeMain("OnUpdate", context, deltaTime);
        luaEnv.Tick();
    }

    public void ContentReady() => InvokeMain("OnContentReady", context);

    public void Dispose()
    {
        DisposeRuntime(notifyUnload: true);
    }

    /// <summary>候选退役跳过 OnUnload，但仍释放已登记的订阅，不能让委托持有已销毁的 LuaEnv。</summary>
    internal void DisposeWithoutUnloadCallback() => DisposeRuntime(notifyUnload: false);

    private void DisposeRuntime(bool notifyUnload)
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (notifyUnload) InvokeMain("OnUnload", context);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MOD:{modId}] OnUnload 执行失败：{ex.Message}");
        }

        context.ReleaseRegistrations();
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

    private string ReadPackageScript(string relativePath)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ModLuaRuntime));
        if (manager != null && manager.IsPreparingResourceReload)
            throw new InvalidOperationException("开放 CS 的 Lua 不能在世界内候选目录执行；请返回主菜单重载 MOD。");
        string fullPath = ModPathUtility.ResolvePackagePath(packageRoot, relativePath, true);
        if (new FileInfo(fullPath).Length > MaximumLuaBytes)
            throw new InvalidDataException($"MOD {modId} Lua 文件超过 2MB 限制：{relativePath}");
        HasLoadedScripts = true;
        return File.ReadAllText(fullPath);
    }

    private byte[] LoadPackageModule(ref string moduleName)
    {
        foreach (string segment in moduleName.Split('.'))
            if (string.IsNullOrWhiteSpace(segment) || segment.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException($"MOD require 名称无效：{moduleName}，请使用 scripts.helper 形式。");
        string relativePath = moduleName.Replace('.', '/') + ".lua";
        string source = ReadPackageScript(relativePath);
        moduleName = $"@{modId}/{relativePath}";
        return Encoding.UTF8.GetBytes(source);
    }

    private LuaTable GetModuleTable(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException($"[MOD:{modId}] Lua 模块脚本路径为空");

        string normalizedPath = relativePath.Replace('\\', '/');
        if (moduleTables.TryGetValue(normalizedPath, out LuaTable table))
            return table;

        string source = ReadPackageScript(normalizedPath);
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
        environment.Set("mod", context);
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
