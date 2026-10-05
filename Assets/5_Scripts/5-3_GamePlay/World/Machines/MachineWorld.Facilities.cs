using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

public static partial class MachineWorld
{
    #region 地面物品输送
    private static readonly RaycastHit2D[] transportHits = new RaycastHit2D[1];

    /// <summary>只读取当前世界的数据格，不因查询输送带而创建世界或加载区块。</summary>
    public static MachineEntity GetTransportAt(Vector2 position)
    {
        MachineEntity node = GetAtCurrentWorld(CellOf(position), 0);
        return node?.Definition.Transport != null ? node : null;
    }

    /// <summary>分段经过实际带格，连续带、转弯和反转都不会因节点遍历顺序重复加速。</summary>
    public static Vector2 TransportGroundItem(Vector2 position, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || !MachineDefinition.Positive(seconds)) return position;
        Vector2 current = position;
        float remaining = Mathf.Min(seconds, .5f);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(LayerMask.GetMask("Collider"));
        for (int segment = 0; segment < 64 && remaining > .00001f; segment++)
        {
            MachineEntity belt = GetTransportAt(current);
            if (belt == null || !belt.Active || belt.SpeedRpm <= 0f) break;
            MachineTransportDefinition transport = belt.Definition.Transport;
            Vector2 center = (Vector2)belt.Cell + Vector2.one * .5f;
            Vector2 offset = WorldTopologyRuntime.ShortestDelta(center, current);
            float speed = transport.Speed * GetWorkEfficiency(belt);
            float step = Mathf.Min(remaining, .1f / speed);
            if (!belt.ConveyorRoute.TryMove(offset, speed * step * Mathf.Sign(belt.Rpm),
                transport.HalfWidth, out Vector2 advanced)) break;
            Vector2 move = advanced - offset;
            float distance = move.magnitude;
            // Physics2D 只给出墙体和实体的阻挡反馈，掉落物位置仍由数据系统持有。
            if (distance > .00001f && Physics2D.CircleCast(current, .06f, move / distance,
                filter, transportHits, distance) > 0)
            {
                float allowed = Mathf.Max(0f, transportHits[0].distance - .01f);
                current += move / distance * allowed;
                break;
            }
            Vector2 destination = WorldTopologyRuntime.NormalizePosition(current + move);
            if (ChunkMgr.ExistingInstance == null ||
                !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(destination, out _)) break;
            current = destination;
            remaining -= step;
        }
        return current;
    }
    #endregion

    #region 设施状态与环境
    private static readonly Dictionary<string, Func<MachineEntity, float>> airflowProviders = new(StringComparer.Ordinal);
    public static event Func<int, string, string, bool> RemoteOperationRequested;

    /// <summary>客户端仅发送操作意图，不能通过面板直接改写机器状态。</summary>
    public static bool RequestOperation(MachineEntity entity, string operation, string argument, Player actor)
    {
        if (!Contains(entity)) return false;
        return GameNetwork.HasStateAuthority ? Execute(entity, operation, argument, actor)
            : RemoteOperationRequested?.Invoke(entity.Id, operation, argument) == true;
    }

    /// <summary>普通工作方块和机械节点使用同一个快照封装，不需要伪造机械模块。</summary>
    public static MachineState ReadMachineState(ItemData snapshot)
    {
        MachineState state = MachinePersistence.Has(snapshot, "core")
            ? MachinePersistence.Read<MachineState>(snapshot, "core") ?? throw new InvalidOperationException("机器核心快照为空。")
            : TryGetModuleData(snapshot, out var module) && module.BitData?.Length > 0
                ? module.GetData<MachineState>() ?? new MachineState() : new MachineState();
        if (state.Hp >= 0f)
            state.Hp = Mathf.Min(state.Hp, ResolveMaximumHp(snapshot)); // 旧机器快照不能超过当前定义与实例品质的耐久上限。
        return state;
    }

    public static void WriteMachineState(ItemData snapshot, MachineState state)
    {
        MachinePersistence.Write(snapshot, "core", state);
        if (TryGetModuleData(snapshot, out var module)) module.WriteData(state);
    }

    public static void NotifyVisualChanged(MachineEntity entity)
    { if (Contains(entity)) CellChanged?.Invoke(entity.Cell); }

    public static bool TryFindContainingInventory(Item actor, ItemData item, out Inventory inventory)
    {
        inventory = null;
        if (graph == null || actor == null || item == null || actor.gameObject.scene.name != worldKey) return false;
        RebuildGraphsIfDirty();
        float range = actor.GetComponentInChildren<Mod_InteractSender>()?.maxInteractDistance ?? Mod_InteractSender.DefaultMaxInteractDistance;
        var candidates = graph.GetInteractionCandidates(CellOf(actor.transform.position), Mathf.CeilToInt(range), false);
        foreach (MachineEntity entity in candidates)
        {
            if (WorldTopologyRuntime.Distance(actor.transform.position, entity.Position) > range) continue;
            foreach (Inventory candidate in MachineInventoryCommands.GetMachineInventories(entity))
                foreach (ItemSlot slot in candidate.Data.itemSlots)
                    if (ReferenceEquals(slot?.itemData, item) || item.Guid != 0 && slot?.itemData?.Guid == item.Guid)
                    { inventory = candidate; return true; }
        }
        return false;
    }

    public static int CountBurningSources(MachineEntity observer, float radius, IReadOnlyList<string> ids, IReadOnlyList<string> moduleIds)
    {
        if (graph == null || !MachineDefinition.Positive(radius)) return 0;
        RebuildGraphsIfDirty();
        int extent = Mathf.CeilToInt(radius), count = 0;
        var seen = new HashSet<int>();
        for (int y = -extent; y <= extent; y++)
        for (int x = -extent; x <= extent; x++)
        {
            MachineEntity candidate = graph.At(observer.Cell + new Vector2Int(x, y), 0);
            if (candidate == null || ReferenceEquals(candidate, observer) || candidate.Logic?.IsBurning != true || !seen.Add(candidate.Id)) continue;
            if (graph.Topology.Distance(new float2(observer.Position.x, observer.Position.y), new float2(candidate.Position.x, candidate.Position.y)) > radius) continue;
            bool matches = false;
            foreach (string id in ids) matches |= string.Equals(id, candidate.Definition.Id, StringComparison.OrdinalIgnoreCase);
            if (!matches && candidate.Snapshot.ModuleDataDic != null)
                foreach (ModuleData module in candidate.Snapshot.ModuleDataDic.Values)
                    foreach (string id in moduleIds) matches |= module?.ID == id;
            if (matches) count++;
        }
        return count;
    }

    /// <summary>命令入口同时供面板、服务端和 MOD 使用；拒绝越权与已过期实体。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Execute(MachineEntity entity, string operation, string argument, Player actor)
    {
        if (!GameNetwork.HasStateAuthority || !Contains(entity)) return false;
        WakeForInteraction(entity);
        if (operation == "inventory.layout") return MachineInventoryCommands.ExecuteLayout(entity, argument);
        if (operation == "inventory.private-layout")
            return MachineInventoryCommands.ExecutePrivateLayout(entity, argument, actor);
        if (operation == "conveyor.orientation") return CycleConveyorOrientation(entity);
        if (entity.Logic != null) return entity.Logic.Execute(operation, argument, actor);
        if (operation == "crank" && entity.Definition.Source == "manual")
        {
            entity.State.ManualSeconds = Mathf.Max(entity.State.ManualSeconds,
                Mathf.Min(MachineCatalog.Settings.ManualReserveSeconds, Mathf.Max(MachineCatalog.Settings.ManualPulseSeconds, MachineCatalog.Settings.TickSeconds * 2f)));
        }
        else if (operation == "work" && entity.Definition.Kind == "clutch")
        { entity.State.Engaged = !entity.State.Engaged; TopologyChanged(entity); }
        else if (operation == "work" && entity.Definition.Kind == "gearbox" && entity.Definition.Ratios.Length > 1)
        { entity.State.RatioIndex = (entity.State.RatioIndex + 1) % entity.Definition.Ratios.Length; TopologyChanged(entity); }
        else if (operation == "work" && entity.Definition.ManualDriveTorque > 0f) return TryManualDrive(entity);
        else if (operation == "work" && MachineDefinition.Positive(entity.Definition.ManualWorkSecondsPerPress))
            return entity.Processor?.AdvanceManually(entity.Definition.ManualWorkSecondsPerPress, actor) == true;
        else return false;
        StateChanged(entity);
        return true;
    }

    /// <summary>注销领域对象前先释放面板，避免休眠后旧 UI 继续修改旧库存。</summary>
    private static void DisposeMachineRuntime(MachineEntity entity)
    {
        if (interactions.Remove(entity.Id, out MachineInteractionTarget target)) target.Dispose();
        entity.Logic?.Dispose();
        entity.Logic = null;
        entity.LogicElapsed = 0f;
    }

    private static void AdvanceFacilities(float seconds)
    {
        foreach (MachineEntity entity in graph.Facilities)
        {
            bool active = false;
            int range = entity.Active ? MachineCatalog.Settings.DeactivationChunks : MachineCatalog.Settings.ActivationChunks;
            foreach (Vector2Int player in players)
            {
                if (!IsFacilityNear(entity, player, range)) continue;
                active = true; break;
            }
            if (active) entity.AwaySeconds = 0f;
            else if (entity.Active)
            {
                entity.AwaySeconds += seconds;
                active = entity.AwaySeconds < MachineCatalog.Settings.UnloadDelaySeconds;
            }
            if (!active)
            {
                if (entity.State != null)
                {
                    CaptureNode(entity);
                    DisposeProcessor(entity);
                    entity.State = null;
                }
                entity.Active = false;
                continue;
            }
            if (entity.State == null) WakeNode(entity);
            entity.Active = true;
            AdvanceFacilityLogic(entity, seconds);
        }
    }

    private static bool IsFacilityNear(MachineEntity entity, Vector2Int player, int range)
        => graph.IsNear(entity.SimulationBounds, player, range);

    private static void AdvanceFacilityLogic(MachineEntity entity, float seconds)
    {
        if (entity.Logic == null) return;
        entity.LogicElapsed += seconds;
        if (entity.LogicElapsed + .00001f < entity.Logic.TickInterval) return;
        float step = entity.LogicElapsed;
        entity.LogicElapsed = 0f;
        entity.Airflow = SampleAirflow(entity);
        entity.Logic.Advance(step);
        if (interactions.TryGetValue(entity.Id, out var target)) target.RefreshPanel();
    }

    /// <summary>鼓风是设施环境输入；炉体不知道齿轮、水车或网络内部结构。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float SampleAirflow(MachineEntity target)
    {
        float value = GetBellowsBoost(target.Position);
        foreach (var provider in airflowProviders.Values)
        {
            float supplied = provider(target);
            if (MachineDefinition.NonNegative(supplied)) value = Mathf.Max(value, supplied);
        }
        return Mathf.Clamp01(value);
    }

    public static IDisposable RegisterAirflow(string id, Func<MachineEntity, float> provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("鼓风来源注册无效。");
        if (airflowProviders.ContainsKey(id)) throw new InvalidOperationException("鼓风来源已注册：" + id);
        airflowProviders.Add(id, provider);
        return new AirflowLease(id, provider);
    }

    private sealed class AirflowLease : IDisposable
    {
        private string id;
        private readonly Func<MachineEntity, float> provider;
        public AirflowLease(string id, Func<MachineEntity, float> provider) { this.id = id; this.provider = provider; }
        public void Dispose()
        {
            if (id != null && airflowProviders.TryGetValue(id, out var active) && active == provider) airflowProviders.Remove(id);
            id = null;
        }
    }

    /// <summary>世界退出只撤销运行态热源；MOD 注册属于资源会话，不能被换维度清空。</summary>
    private static void ClearFacilityEnvironment() { }
    #endregion
}
