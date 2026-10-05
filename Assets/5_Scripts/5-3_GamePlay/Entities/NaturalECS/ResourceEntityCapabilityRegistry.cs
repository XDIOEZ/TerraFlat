using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Unity.Entities;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    #region 资源能力扩展契约

    public delegate bool ResourceEntityCapabilityCompiler(RuntimeItemModuleDefinition module,
        out ResourceEntityCapability capability, out string reason);

    /// <summary>运行时加载的 MOD 用普通 C# 状态扩展 Entity，不要求 Unity 重新注册原生组件类型。</summary>
    public sealed class ResourceEntityExtensionState : IComponentData
    {
        internal readonly Dictionary<string, object> Modules = new(StringComparer.Ordinal);
    }

    /// <summary>编译结果只保存组件组合和生命周期回调，批量玩法由共享 World 的运行器执行。</summary>
    public sealed class ResourceEntityCapability
    {
        internal readonly ComponentType[] Types;
        public IReadOnlyList<ComponentType> ComponentTypes { get; }
        public Action<ResourceEntityCapabilityContext> Initialize { get; }
        public Action<ResourceEntityCapabilityContext> Capture { get; }
        public Action<ResourceEntityCapabilityContext> Release { get; }

        public ResourceEntityCapability(ComponentType[] componentTypes,
            Action<ResourceEntityCapabilityContext> initialize,
            Action<ResourceEntityCapabilityContext> capture = null,
            Action<ResourceEntityCapabilityContext> release = null)
        {
            if (componentTypes == null) throw new ArgumentNullException(nameof(componentTypes));
            Types = (ComponentType[])componentTypes.Clone();
            ComponentTypes = Array.AsReadOnly(Types);
            Initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
            Capture = capture;
            Release = release;
        }

        public ResourceEntityCapability(Action<ResourceEntityCapabilityContext> initialize,
            Action<ResourceEntityCapabilityContext> capture = null,
            Action<ResourceEntityCapabilityContext> release = null)
            : this(Array.Empty<ComponentType>(), initialize, capture, release) { }
    }

    /// <summary>初始化和保存都使用同一实体及模块快照，扩展不能额外创建资源 World。</summary>
    public readonly struct ResourceEntityCapabilityContext
    {
        public World World { get; }
        public Entity Entity { get; }
        public NaturalEntityHandle Handle { get; }
        public RuntimeItemDefinition Definition { get; }
        public RuntimeItemModuleDefinition Module { get; }
        public ItemData Snapshot { get; }
        public bool FreshlyPlanted { get; }
        public EntityManager Manager => World.EntityManager;

        internal ResourceEntityCapabilityContext(World world, Entity entity, NaturalEntityHandle handle,
            RuntimeItemDefinition definition, RuntimeItemModuleDefinition module, ItemData snapshot, bool freshlyPlanted)
        {
            World = world; Entity = entity; Handle = handle; Definition = definition;
            Module = module; Snapshot = snapshot; FreshlyPlanted = freshlyPlanted;
        }

        public void NotifyChanged() => NaturalEntityEcsService.NotifyChanged(Handle);

        public T GetState<T>() where T : class
        {
            var state = Manager.GetComponentObject<ResourceEntityExtensionState>(Entity);
            return state.Modules.TryGetValue(Module.StableName, out object value) ? value as T : null;
        }

        public void SetState(object value)
        {
            var state = Manager.GetComponentObject<ResourceEntityExtensionState>(Entity);
            if (value == null) state.Modules.Remove(Module.StableName);
            else state.Modules[Module.StableName] = value;
        }
    }

    #endregion

    /// <summary>C# MOD 在内容使用前注册能力并持有租约，注销只影响后续编译，现存实体持有冻结计划。</summary>
    public static class ResourceEntityCapabilityRegistry
    {
        #region 注册与定义版本

        internal sealed class Registration : IDisposable
        {
            internal string PrefabId;
            internal ResourceEntityCapabilityCompiler Compiler;
            internal Func<World, IWorldEntityRuntimeModule> RuntimeFactory;
            internal World RuntimeWorld;
            internal RuntimeAdapter Runtime;

            public void Dispose()
            {
                if (entries.TryGetValue(PrefabId, out Registration current) && ReferenceEquals(current, this))
                {
                    entries.Remove(PrefabId);
                    Revision++;
                }
            }
        }

        private static readonly Dictionary<string, Registration> entries = new(StringComparer.OrdinalIgnoreCase);
        public static ulong Revision { get; private set; }

        public static bool IsRegistered(string prefabId) =>
            !string.IsNullOrWhiteSpace(prefabId) && entries.ContainsKey(prefabId.Trim());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { entries.Clear(); Revision++; }

        public static IDisposable Register(string prefabId, ResourceEntityCapabilityCompiler compiler,
            Func<World, IWorldEntityRuntimeModule> runtimeFactory = null)
        {
            if (string.IsNullOrWhiteSpace(prefabId)) throw new ArgumentException("资源能力必须有稳定模块地址。", nameof(prefabId));
            prefabId = prefabId.Trim();
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));
            if (NaturalEntityEcsProfileCompiler.HasBuiltin(prefabId) || entries.ContainsKey(prefabId))
                throw new InvalidOperationException($"资源能力 {prefabId} 已注册，禁止覆盖其它能力。");
            var registration = new Registration { PrefabId = prefabId, Compiler = compiler, RuntimeFactory = runtimeFactory };
            entries.Add(prefabId, registration);
            Revision++;
            return registration;
        }

        internal static bool TryCompile(RuntimeItemModuleDefinition module, out CompiledResourceCapability compiled,
            out bool recognized, out string reason)
        {
            compiled = null;
            reason = null;
            recognized = entries.TryGetValue(module.PrefabId?.Trim() ?? string.Empty, out Registration registration);
            if (!recognized) return false;
            try
            {
                if (!registration.Compiler(module, out ResourceEntityCapability capability, out reason))
                {
                    reason ??= "扩展编译器拒绝了配置。";
                    return false;
                }
                if (capability == null) { reason = "扩展编译器未返回能力计划。"; return false; }
                compiled = new CompiledResourceCapability { Module = module, Capability = capability, Registration = registration };
                return true;
            }
            catch (Exception exception)
            {
                reason = $"资源能力 {module.StableName}/{module.PrefabId} 编译失败：{exception.Message}";
                return false;
            }
        }

        #endregion

        #region 共享世界运行器

        internal static void EnsureRuntime(CompiledResourceCapability compiled, World world)
        {
            Registration registration = compiled.Registration;
            if (registration.RuntimeFactory == null || ReferenceEquals(registration.RuntimeWorld, world)) return;
            IWorldEntityRuntimeModule module = registration.RuntimeFactory(world)
                ?? throw new InvalidOperationException($"资源能力 {registration.PrefabId} 未创建运行器。");
            var adapter = new RuntimeAdapter(registration, module);
            try { WorldEntityRuntime.Register(world, adapter); }
            catch { module.ReleaseEntities(); throw; }
            registration.Runtime = adapter;
            registration.RuntimeWorld = world;
        }

        internal sealed class RuntimeAdapter : IWorldEntityRuntimeModule, IWorldEntityPostSimulationModule
        {
            private readonly Registration registration;
            private readonly IWorldEntityRuntimeModule module;
            internal RuntimeAdapter(Registration registration, IWorldEntityRuntimeModule module)
            { this.registration = registration; this.module = module; }
            public string RuntimeModuleId => "entity.resource-extension/" + registration.PrefabId;
            public void TickEntities(float deltaTime)
            {
                if (GameNetwork.HasStateAuthority) module.TickEntities(deltaTime);
            }
            public void CompleteEntityJobs() => module.CompleteEntityJobs();
            public void AfterEntitySimulation(float deltaTime)
            {
                if (GameNetwork.HasStateAuthority && module is IWorldEntityPostSimulationModule post)
                    post.AfterEntitySimulation(deltaTime);
            }
            public void ReleaseEntities()
            {
                try { module.ReleaseEntities(); }
                finally { registration.RuntimeWorld = null; registration.Runtime = null; }
            }
        }

        #endregion
    }

    internal sealed class CompiledResourceCapability
    {
        public RuntimeItemModuleDefinition Module;
        public ResourceEntityCapability Capability;
        public ResourceEntityCapabilityRegistry.Registration Registration;
    }

    public static partial class NaturalEntityEcsService
    {
        #region 扩展组件生命周期

        private sealed partial class Record
        {
            public CompiledResourceCapability[] InstalledExtensions = Array.Empty<CompiledResourceCapability>();
            public int InitializedExtensionCount;
            public RuntimeItemDefinition ExtensionDefinition;
            public ItemData ExtensionSnapshot;
        }

        private static ResourceEntityCapabilityContext ExtensionContext(Record record,
            CompiledResourceCapability extension, bool freshlyPlanted = false) => new(
                simulation.World, simulation.GetEntity(record.Handle.Id), record.Handle,
                record.ExtensionDefinition, extension.Module, record.ExtensionSnapshot, freshlyPlanted);

        private static void InstallExtensionModules(Record record, bool freshlyPlanted)
        {
            if (record.InstalledExtensions.Length == 0 && record.Profile.Extensions.Count == 0) return;
            var nextTypes = new HashSet<Type>();
            foreach (CompiledResourceCapability extension in record.Profile.Extensions)
                foreach (ComponentType type in extension.Capability.Types) nextTypes.Add(type.GetManagedType());
            CompiledResourceCapability[] previous = record.InstalledExtensions;
            ReleaseExtensionModules(record);
            EntityManager manager = simulation.Manager;
            Entity entity = simulation.GetEntity(record.Handle.Id);
            foreach (CompiledResourceCapability extension in previous)
                foreach (ComponentType type in extension.Capability.Types)
                    if (!nextTypes.Contains(type.GetManagedType()) && manager.HasComponent(entity, type))
                        manager.RemoveComponent(entity, type);
            if (record.Profile.Extensions.Count > 0)
            {
                if (!manager.HasComponent<ResourceEntityExtensionState>(entity))
                    manager.AddComponentObject(entity, new ResourceEntityExtensionState());
                else if (manager.GetComponentObject<ResourceEntityExtensionState>(entity) == null)
                    manager.SetComponentData(entity, new ResourceEntityExtensionState());
            }
            else if (manager.HasComponent<ResourceEntityExtensionState>(entity))
                manager.RemoveComponent<ResourceEntityExtensionState>(entity);
            record.InstalledExtensions = record.Profile.Extensions.ToArray();
            record.ExtensionDefinition = record.Profile.Definition;
            record.ExtensionSnapshot = record.Snapshot;
            foreach (CompiledResourceCapability extension in record.InstalledExtensions)
            {
                foreach (ComponentType type in extension.Capability.Types)
                    if (!manager.HasComponent(entity, type)) manager.AddComponent(entity, type);
                // 初始化中途失败也释放已进入回调的能力，未开始的能力不会收到释放通知。
                record.InitializedExtensionCount++;
                extension.Capability.Initialize(ExtensionContext(record, extension, freshlyPlanted));
                ResourceEntityCapabilityRegistry.EnsureRuntime(extension, simulation.World);
            }
            QueuePublication(record.Handle.Id);
        }

        private static void CaptureExtensionModules(Record record)
        {
            for (int i = 0; i < record.InitializedExtensionCount; i++)
            {
                CompiledResourceCapability extension = record.InstalledExtensions[i];
                extension.Capability.Capture?.Invoke(ExtensionContext(record, extension));
            }
        }

        private static void ReleaseExtensionModules(Record record)
        {
            if (simulation?.IsCreated == true && simulation.Contains(record.Handle.Id))
                for (int i = 0; i < record.InitializedExtensionCount; i++)
                {
                    CompiledResourceCapability extension = record.InstalledExtensions[i];
                    try { extension.Capability.Release?.Invoke(ExtensionContext(record, extension)); }
                    catch (Exception exception) { Debug.LogError($"[NaturalEntities] 扩展 {extension.Module.StableName} 释放失败：{exception}"); }
                    finally
                    {
                        Entity entity = simulation.GetEntity(record.Handle.Id);
                        if (simulation.Manager.HasComponent<ResourceEntityExtensionState>(entity))
                            simulation.Manager.GetComponentObject<ResourceEntityExtensionState>(entity)?.Modules.Remove(extension.Module.StableName);
                    }
                }
            record.InstalledExtensions = Array.Empty<CompiledResourceCapability>();
            record.InitializedExtensionCount = 0;
            record.ExtensionDefinition = null;
            record.ExtensionSnapshot = null;
        }

        /// <summary>扩展查询实体前核对世界代际，原生句柄仅在当前世界生命周期内有效。</summary>
        public static bool TryGetEntity(NaturalEntityHandle handle, out World world, out Entity entity)
        {
            world = null; entity = Entity.Null;
            if (!Contains(handle)) return false;
            world = simulation.World; entity = simulation.GetEntity(handle.Id);
            return true;
        }

        #endregion
    }
}
