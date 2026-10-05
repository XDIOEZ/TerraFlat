using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>C# MOD 生命周期；托管代码具有游戏进程权限，不是 Lua 沙箱。</summary>
public interface IManagedGameMod : IDisposable
{
    void Initialize(ManagedModContext context);
    void ContentReady();
}

public sealed class ManagedModContext : IDisposable
{
    #region 生命周期资源
    public string ModId { get; }
    public string PackagePath { get; }
    private readonly List<IDisposable> registrations = new();
    private bool disposed;
    internal ManagedModContext(string modId, string path) { ModId = modId; PackagePath = path; }
    public T Track<T>(T registration) where T : IDisposable
    {
        if (disposed) throw new ObjectDisposedException(nameof(ManagedModContext));
        if (registration == null) throw new ArgumentNullException(nameof(registration));
        registrations.Add(registration);
        return registration;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int i = registrations.Count - 1; i >= 0; i--)
            try { registrations[i].Dispose(); } catch (Exception error) { Debug.LogException(error); }
        registrations.Clear();
    }
    #endregion
}

public sealed partial class ModRuntimeManager
{
    #region 托管 MOD 加载
    private sealed class ManagedSession
    {
        public IManagedGameMod Entry;
        public ManagedModContext Context;
    }
    private readonly Dictionary<string, ModManagedAssemblyStore.PackageCode> preparedManaged = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ManagedSession> managedSessions = new();
    public bool HasManagedMods => managedSessions.Count > 0;

    private void PrepareManagedPackages(List<ModPackage> packages)
    {
        preparedManaged.Clear();
        foreach (ModPackage package in packages.Where(value => value.Manifest.Managed != null))
        {
            if (preparingResourceReload)
                throw new InvalidOperationException("C# / Harmony MOD 不得在原位资源候选会话中启用；请返回主菜单重新加载。");
#if ENABLE_IL2CPP
            throw new PlatformNotSupportedException("此构建使用 IL2CPP，不能加载 C# / Harmony MOD：" + package.Manifest.Id);
#else
            var code = ModManagedAssemblyStore.ReadPackage(package.RootPath, package.Manifest.Managed);
            // 启动游戏即默认允许已启用的 C# MOD，不再单独核对指纹授权。
            preparedManaged.Add(package.Manifest.Id, code);
#endif
        }
    }

    private void InitializeManaged(ModPackage package)
    {
        if (package.Manifest.Managed == null) return;
        if (!preparedManaged.TryGetValue(package.Manifest.Id, out var code)) throw new InvalidOperationException("C# MOD 尚未准备程序集。");
        Assembly assembly = ModManagedAssemblyStore.LoadTrusted(code);
        Type type = assembly.GetType(package.Manifest.Managed.EntryType, true, false);
        if (type.IsAbstract || !typeof(IManagedGameMod).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidOperationException("C# MOD 入口必须是带公开无参构造的 IManagedGameMod：" + type.FullName);
        var session = new ManagedSession
        {
            Context = new ManagedModContext(package.Manifest.Id, package.RootPath),
            Entry = (IManagedGameMod)Activator.CreateInstance(type)
        };
        managedSessions.Add(session);
        session.Entry.Initialize(session.Context);
    }

    private void PublishManagedContentReady()
    { foreach (ManagedSession session in managedSessions) session.Entry.ContentReady(); }

    private void UnloadManaged()
    {
        for (int i = managedSessions.Count - 1; i >= 0; i--)
        {
            ManagedSession session = managedSessions[i];
            try { session.Entry.Dispose(); } catch (Exception error) { Debug.LogException(error); }
            finally { session.Context.Dispose(); }
        }
        managedSessions.Clear();
        preparedManaged.Clear();
    }
    #endregion
}
