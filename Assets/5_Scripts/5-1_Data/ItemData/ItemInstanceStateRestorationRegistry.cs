using System;
using System.Collections.Generic;

/// <summary>模块在所有状态恢复后补充纯数据派生值，不能改模块布局或建立运行资源。</summary>
public interface IItemInstanceStateRestorer
{
    string ModuleId { get; }
    void Restore(ItemData itemData, ModuleData moduleData);
}

public static class ItemInstanceStateRestorationRegistry
{
    #region 恢复扩展注册

    private static readonly Dictionary<string, IItemInstanceStateRestorer> Restorers = new(StringComparer.Ordinal);
    private static readonly object RegistryLock = new();

    public static IDisposable Register(IItemInstanceStateRestorer restorer)
    {
        if (restorer == null || string.IsNullOrWhiteSpace(restorer.ModuleId))
            throw new ArgumentException("实例恢复扩展必须声明模块 ID。", nameof(restorer));
        string moduleId = restorer.ModuleId;
        lock (RegistryLock)
        {
            if (Restorers.ContainsKey(moduleId))
                throw new InvalidOperationException($"模块 {moduleId} 已注册实例恢复扩展。");
            Restorers.Add(moduleId, restorer);
        }
        return new RegistrationLease(moduleId, restorer);
    }

    internal static void Restore(ItemData data)
    {
        if (data.SharedConfiguration == null || data.ModuleDataDic == null) return;
        var pending = new List<(IItemInstanceStateRestorer Restorer, ModuleData Module)>();
        lock (RegistryLock)
            foreach (ModuleData module in data.ModuleDataDic.Values)
                if (module != null && Restorers.TryGetValue(module.ModuleId, out var restorer))
                    pending.Add((restorer, module));
        // 扩展回调在锁外执行，保存与网络恢复都使用同一纯数据契约。
        foreach (var entry in pending) entry.Restorer.Restore(data, entry.Module);
    }

    private sealed class RegistrationLease : IDisposable
    {
        private readonly string moduleId;
        private readonly IItemInstanceStateRestorer restorer;
        private bool disposed;

        public RegistrationLease(string moduleId, IItemInstanceStateRestorer restorer)
        {
            this.moduleId = moduleId;
            this.restorer = restorer;
        }

        public void Dispose()
        {
            lock (RegistryLock)
            {
                if (disposed) return;
                if (Restorers.TryGetValue(moduleId, out var current) && ReferenceEquals(current, restorer))
                    Restorers.Remove(moduleId);
                disposed = true;
            }
        }
    }

    #endregion
}
