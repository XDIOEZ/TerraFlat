using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>注册整类机器玩法，不把炉温、燃料或端口拆成微型模块；代码 MOD 可替换工厂。</summary>
public static class MachineLogicRegistry
{
    #region 领域工厂
    private sealed class Registration
    {
        public string Id;
        public Type AuthoringType;
        public Func<MachineEntity, MachineLogic> Factory;
        public FluidDeviceBehavior FluidBehavior;
        public Registration Previous;
        public bool Released;
    }

    private sealed class Lease : IDisposable
    {
        private Registration current;
        public Lease(Registration current) { this.current = current; }
        public void Dispose()
        {
            if (current == null) return;
            current.Released = true;
            if (registrations.TryGetValue(current.Id, out Registration active) && ReferenceEquals(current, active))
            {
                // MOD 可按任意顺序卸载，跳过已释放的覆盖层，不能复活旧工厂。
                Registration previous = current.Previous;
                while (previous != null && previous.Released) previous = previous.Previous;
                if (previous == null) registrations.Remove(current.Id);
                else registrations[current.Id] = previous;
                compiled.Clear();
                FluidDeviceGeneration++;
                MachineWorld.InvalidateFluidDeviceStrategies();
            }
            current = null;
        }
    }

    private static readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (RuntimeItemDefinition Source, MachineDefinition Result)> compiled = new(StringComparer.Ordinal);
    private static bool initialized;
    public static long FluidDeviceGeneration { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    { registrations.Clear(); compiled.Clear(); initialized = false; FluidDeviceGeneration++; MachineWorld.InvalidateFluidDeviceStrategies(); }

    private static void EnsureBuiltIns()
    {
        if (initialized) return;
        initialized = true;
        Add("workbench", typeof(Mod_MakeTable), entity => new WorkbenchLogic(entity));
        Add("furnace", typeof(Mod_Furnace), entity => new FurnaceLogic(entity));
        Add("compost", typeof(Mod_CompostBin), entity => new CompostLogic(entity));
        Add("drying", typeof(Mod_Meatrack), entity => new DryingRackLogic(entity));
        Add("storage", typeof(Mod_Inventory), entity => new StorageLogic(entity));
        Add("manual-processing", typeof(Mod_ManualProcessor), entity => new ManualProcessingLogic(entity));
        Add("hand-drill", typeof(Mod_HandDrill), entity => new HandDrillLogic(entity));
        Add("fire-drill", typeof(Mod_FireDrill), entity => new FireDrillLogic(entity));
        Add("mortar", typeof(Mod_Mortar), entity => new MortarLogic(entity));
        Add("vessel", typeof(Mod_WaterVessel), entity => new VesselLogic(entity));
        Add("fluid", null, entity => new FluidMachineLogic(entity));
        Add("electric-heater", null, entity => new ElectricHeaterLogic(entity));
        AddFluidDevice("pipe", new FluidTransitBehavior());
        AddFluidDevice("outlet", new FluidOutletBehavior());
        AddFluidDevice("valve", new FluidValveBehavior());
        AddFluidDevice("selector", new FluidSelectorBehavior());
        AddFluidDevice("tank", new FluidStorageBehavior());
        AddFluidDevice("gas-pump", new FluidGasPumpBehavior());
        AddFluidDevice("liquid-pump", new FluidLiquidPumpBehavior());
        AddFluidDevice("compressor", new FluidCompressorBehavior());
        AddFluidDevice("filter", new FluidFilterBehavior());
        AddFluidDevice("electrolyzer", new FluidElectrolyzerBehavior());
        AddFluidDevice("engine", new FluidEngineBehavior());
        AddFluidDevice("mechanical-probe", new FluidPressureProbeBehavior(false));
        AddFluidDevice("electronic-probe", new FluidPressureProbeBehavior(true));
    }

    private static void Add(string id, Type authoring, Func<MachineEntity, MachineLogic> factory)
        => registrations.Add(id, new Registration { Id = id, AuthoringType = authoring, Factory = factory });

    private static void AddFluidDevice(string kind, FluidDeviceBehavior behavior)
        => registrations.Add("fluid-device:" + kind, new Registration
        { Id = "fluid-device:" + kind, Factory = entity => new FluidMachineLogic(entity), FluidBehavior = behavior });

    /// <summary>流体策略复用领域工厂的覆盖租约，目录检查只查询登记，不创建设备。</summary>
    public static IDisposable RegisterFluidDevice(string kind, FluidDeviceBehavior behavior, bool replace = false)
    {
        if (string.IsNullOrWhiteSpace(kind) || behavior == null) throw new ArgumentException("流体设备策略登记无效。");
        return RegisterCore("fluid-device:" + kind, entity => new FluidMachineLogic(entity), null, replace, behavior);
    }

    public static bool IsFluidDeviceRegistered(string kind)
    { EnsureBuiltIns(); return registrations.TryGetValue("fluid-device:" + kind, out var entry) && entry.FluidBehavior != null; }

    public static FluidDeviceBehavior GetFluidDeviceBehavior(string kind)
    {
        EnsureBuiltIns();
        return registrations.TryGetValue("fluid-device:" + kind, out var entry) && entry.FluidBehavior != null
            ? entry.FluidBehavior : throw new InvalidOperationException("流体设备策略未登记：" + kind);
    }

    /// <summary>MOD 可以注册带自定义配置模块的机器，或仅通过 MachineDefinition.LogicId 选择领域工厂。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDisposable Register(string id, Func<MachineEntity, MachineLogic> factory,
        Type authoringType = null, bool replace = false)
        => RegisterCore(id, factory, authoringType, replace, null);

    private static IDisposable RegisterCore(string id, Func<MachineEntity, MachineLogic> factory,
        Type authoringType, bool replace, FluidDeviceBehavior fluidBehavior)
    {
        EnsureBuiltIns();
        if (string.IsNullOrWhiteSpace(id) || factory == null ||
            authoringType != null && !typeof(Module).IsAssignableFrom(authoringType))
            throw new ArgumentException("机器玩法注册参数无效。");
        registrations.TryGetValue(id, out Registration previous);
        if (previous != null && !replace) throw new InvalidOperationException("机器玩法已注册：" + id);
        var entry = new Registration
        {
            Id = id, Factory = factory, AuthoringType = authoringType ?? previous?.AuthoringType,
            FluidBehavior = fluidBehavior,
            Previous = previous
        };
        registrations[id] = entry;
        compiled.Clear();
        FluidDeviceGeneration++;
        MachineWorld.InvalidateFluidDeviceStrategies();
        return new Lease(entry);
    }

    /// <summary>资源会话结束后释放配置模板引用；注册工厂由所属 MOD 的租约单独管理。</summary>
    internal static void ClearContentCache() => compiled.Clear();

    /// <summary>内容预检与 MOD 可查询领域工厂是否存在，不创建机器或修改运行态。</summary>
    public static bool IsRegistered(string id)
    {
        EnsureBuiltIns();
        return !string.IsNullOrWhiteSpace(id) && registrations.ContainsKey(id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MachineLogic Create(MachineEntity entity)
    {
        EnsureBuiltIns();
        if (string.IsNullOrWhiteSpace(entity.Definition.LogicId)) return null;
        if (!registrations.TryGetValue(entity.Definition.LogicId, out Registration entry))
            throw new InvalidOperationException("机器领域工厂缺失：" + entity.Definition.LogicId);
        entity.Definition.Content ??= new MachineContent(GameRes.ExistingInstance.ItemDefinitions[entity.Definition.Id]);
        return entry.Factory(entity) ?? throw new InvalidOperationException("机器领域工厂返回空对象：" + entry.Id);
    }

    /// <summary>仅对已配置为落地建筑的内容编译，不把玩家背包或手持工具注册进机器世界。</summary>
    public static MachineDefinition ResolveContentDefinition(string id)
    {
        EnsureBuiltIns();
        GameRes resources = GameRes.ExistingInstance;
        if (string.IsNullOrWhiteSpace(id) || resources == null || !resources.TryGetItemDefinition(id, out RuntimeItemDefinition source)) return null;
        if (compiled.TryGetValue(id, out var cached) && ReferenceEquals(cached.Source, source)) return cached.Result;
        ItemData template = source.CreateItemData();
        if (!Mod_Building.TryReadBuildingData(template, out _, out var building) ||
            building.Role != BuildingRole.PlacedBuilding)
        { compiled[id] = (source, null); return null; }

        MachineContent content = new(source);
        Registration selected = null;
        foreach (Registration entry in registrations.Values)
        {
            if (entry.AuthoringType == null || !content.Has(entry.AuthoringType)) continue;
            if (selected != null)
                throw new InvalidOperationException("机器定义包含多个主玩法，请注册一个内聚的专用工厂：" + id);
            selected = entry;
        }
        MachineDefinition result = selected == null ? null : new MachineDefinition
        {
            Id = id, Kind = selected.Id, LogicId = selected.Id, Ports = "none",
            CastVisualShadows = true, Content = content
        };
        compiled[id] = (source, result);
        return result;
    }
    #endregion
}
