using System;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using Unity.Entities;

namespace Example.ResourceCapabilities
{
    public sealed class ModEntry : IManagedGameMod
    {
        #region 能力注册与编译

        private IDisposable registration;
        private ResinRuntime runtime;

        public void Initialize(ManagedModContext context)
        {
            registration = ResourceEntityCapabilityRegistry.Register(
                context.ModId + ":resin", CompileResin, GetRuntime);
        }

        public void ContentReady() { }
        public void Dispose() { registration?.Dispose(); registration = null; }

        private ResinRuntime GetRuntime(World world)
        {
            if (runtime == null || !ReferenceEquals(runtime.World, world)) runtime = new ResinRuntime(world);
            return runtime;
        }

        private bool CompileResin(RuntimeItemModuleDefinition module,
            out ResourceEntityCapability capability, out string reason)
        {
            capability = null;
            reason = null;
            JObject parameters = string.IsNullOrWhiteSpace(module.ParametersJson)
                ? new JObject() : JObject.Parse(module.ParametersJson);
            float capacity = parameters.Value<float?>("capacity") ?? 20f;
            float perSecond = parameters.Value<float?>("perSecond") ?? 1f;
            if (float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity <= 0f ||
                float.IsNaN(perSecond) || float.IsInfinity(perSecond) || perSecond < 0f)
            { reason = "树脂容量必须为有限正数，生长速度必须为有限非负数。"; return false; }

            capability = new ResourceEntityCapability(
                initialize: context =>
                {
                    // MOD 状态是普通 C# 对象，挂在已有 Entity 的通用扩展组件内。
                    var saved = (Ex_ModData)context.Snapshot.ModuleDataDic[context.Module.StableName];
                    ResinState state = saved.GetData<ResinState>() ?? new ResinState();
                    if (float.IsNaN(state.Amount) || float.IsInfinity(state.Amount)) state.Amount = 0f;
                    state.Amount = Math.Clamp(state.Amount, 0f, capacity);
                    state.Capacity = capacity;
                    state.PerSecond = perSecond;
                    context.SetState(state);
                    GetRuntime(context.World).States[(context.Entity, context.Module.StableName)] = state;
                },
                capture: context =>
                {
                    var saved = (Ex_ModData)context.Snapshot.ModuleDataDic[context.Module.StableName];
                    // 存档只记录实例树脂量，容量和速度每次从当前 JSON 配置读取。
                    saved.WriteData(new ResinState { Amount = context.GetState<ResinState>().Amount });
                },
                release: context => GetRuntime(context.World).States.Remove((context.Entity, context.Module.StableName)));
            return true;
        }

        #endregion
    }

    public sealed class ResinState
    {
        public float Amount;
        internal float Capacity, PerSecond;
    }

    internal sealed class ResinRuntime : IWorldEntityRuntimeModule
    {
        #region 共享世界批次推进

        public World World { get; }
        public readonly Dictionary<(Entity Entity, string Module), ResinState> States = new();
        private float pendingSeconds;
        public string RuntimeModuleId => "example.resourcecapabilities:resin";
        public ResinRuntime(World world) { World = world; }

        public void TickEntities(float deltaTime)
        {
            pendingSeconds += deltaTime;
            if (pendingSeconds < 0.25f) return;
            foreach (ResinState state in States.Values)
                state.Amount = Math.Min(state.Capacity, state.Amount + state.PerSecond * pendingSeconds);
            pendingSeconds = 0f;
        }

        public void CompleteEntityJobs() { }
        public void ReleaseEntities() { States.Clear(); pendingSeconds = 0f; }

        #endregion
    }
}
