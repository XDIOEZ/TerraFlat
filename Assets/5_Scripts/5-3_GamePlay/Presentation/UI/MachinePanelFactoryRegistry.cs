using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>按稳定机器玩法 ID 注册表现会话，注册表不保存机器或面板实例。</summary>
public static class MachinePanelFactoryRegistry
{
    #region 工厂注册与租约

    private sealed class Registration
    {
        public string Id;
        public Func<MachineEntity, IMachinePanelSession> Factory;
        public Registration Previous;
        public bool Released;
    }

    private sealed class Lease : IDisposable
    {
        private Registration registration;
        public Lease(Registration registration) => this.registration = registration;

        public void Dispose()
        {
            if (registration == null) return;
            registration.Released = true;
            if (registrations.TryGetValue(registration.Id, out Registration active) &&
                ReferenceEquals(active, registration))
            {
                // 乱序卸载时跳过已释放的覆盖层，避免重新启用已卸载的工厂。
                Registration previous = registration.Previous;
                while (previous != null && previous.Released) previous = previous.Previous;
                if (previous == null) registrations.Remove(registration.Id);
                else registrations[registration.Id] = previous;
            }
            registration = null;
        }
    }

    private static readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        registrations.Clear();
        initialized = false;
    }

    private static void EnsureBuiltIns()
    {
        if (initialized) return;
        initialized = true;
        MachinePanelBuiltIns.Register();
    }

    internal static void AddBuiltIn(string id, Func<MachineEntity, IMachinePanelSession> factory)
        => registrations.Add(id, new Registration { Id = id, Factory = factory });

    /// <summary>专属表现与所属 MOD 一起注册和释放；替换领域玩法时可同时替换其面板工厂。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDisposable Register(string logicId, Func<MachineEntity, IMachinePanelSession> factory,
        bool replace = false)
    {
        EnsureBuiltIns();
        if (string.IsNullOrWhiteSpace(logicId) || factory == null)
            throw new ArgumentException("机器面板工厂注册参数无效。");
        registrations.TryGetValue(logicId, out Registration previous);
        if (previous != null && !replace)
            throw new InvalidOperationException("机器面板工厂已注册：" + logicId);
        var registration = new Registration { Id = logicId, Factory = factory, Previous = previous };
        registrations[logicId] = registration;
        return new Lease(registration);
    }

    #endregion

    #region 会话创建

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IMachinePanelSession Create(MachineEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (entity.Logic == null) throw new InvalidOperationException("机器领域状态尚未恢复。");
        EnsureBuiltIns();
        string id = entity.Definition.LogicId;
        if (!string.IsNullOrWhiteSpace(id) && registrations.TryGetValue(id, out Registration registration))
            return registration.Factory(entity) ?? throw new InvalidOperationException("机器面板工厂返回空会话：" + id);
        return new MachinePanelSession(entity);
    }

    #endregion
}
