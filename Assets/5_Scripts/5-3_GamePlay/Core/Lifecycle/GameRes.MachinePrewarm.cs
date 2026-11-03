using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FlatWorld.Gameplay.Building;
using UnityEngine;

public partial class GameRes
{
    #region 机器快照与炉体缓存预备

    /// <summary>进入世界前只预备私有数据和空库存，不放置设施或推进真实燃烧。</summary>
    private IEnumerator PrewarmMachineResources()
    {
        if (preparingInPlaceReload) yield break;

        // 首次放置才会走到的机器与建筑方法，在主菜单资源阶段分帧完成 Mono JIT。
        yield return PrewarmMachinePlacementCode();

        var stateSample = new Data_GeneralItem { IDName = "preparation:machine" };
        MachinePersistence.Write(stateSample, "core", new MachineState());
        if (ShouldYieldResourceWork()) yield return null;
        _ = MachinePersistence.Read<MachineState>(stateSample, "core");
        if (ShouldYieldResourceWork()) yield return null;

        MachinePersistence.Write(stateSample, "furnace", CreateEmptyFurnacePreparationState());
        if (ShouldYieldResourceWork()) yield return null;
        _ = MachinePersistence.Read<FurnaceRuntimeState>(stateSample, "furnace");
        if (ShouldYieldResourceWork()) yield return null;

        // 空图只创建自身的索引与泛型缓存，不登记节点、网络或世界服务。
        var noNodes = Array.Empty<MachineEntity>();
        new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, default(WorldTopologyDomain)).Rebuild(noNodes);
        if (ShouldYieldResourceWork()) yield return null;
        new ElectricalNetworkGraph(default).Rebuild(noNodes);
        if (ShouldYieldResourceWork()) yield return null;
        new FluidNetworkGraph(static cell => cell).Rebuild(noNodes);
        if (ShouldYieldResourceWork()) yield return null;

        bool nonEmptyGraphPrepared = false;
        foreach (RuntimeItemDefinition source in ItemDefinitions.Values)
        {
            bool hasFurnace = false;
            foreach (RuntimeItemModuleAssemblyPlan assembly in source.ModuleAssemblies)
                if (assembly.ModulePrefab != null && assembly.ModulePrefab.GetComponentInChildren<Mod_Furnace>(true) != null)
                { hasFurnace = true; break; }
            if (!hasFurnace)
            {
                if (ShouldYieldResourceWork()) yield return null;
                continue;
            }

            MachineDefinition definition = MachineCatalog.Get(source.Id);
            if (definition?.LogicId != "furnace")
            {
                if (ShouldYieldResourceWork()) yield return null;
                continue;
            }
            MachineContent.GetOrCreateDefinitionContent(definition, this);
            if (ShouldYieldResourceWork()) yield return null;
            ItemData instance = source.CreateItemData();
            if (ShouldYieldResourceWork()) yield return null;
            ItemData snapshot = ItemInstanceDataFactory.CloneRuntime(instance);
            if (ShouldYieldResourceWork()) yield return null;
            MachinePersistence.Write(snapshot, "core", new MachineState());
            if (ShouldYieldResourceWork()) yield return null;
            MachinePersistence.Write(snapshot, "furnace", CreateEmptyFurnacePreparationState());
            if (ShouldYieldResourceWork()) yield return null;
            var entity = new MachineEntity { Definition = definition, Snapshot = snapshot, State = new MachineState() };
            if (ShouldYieldResourceWork()) yield return null;

            if (!nonEmptyGraphPrepared && definition.Fluid == null)
            {
                // 非空图的索引与端口路径必须用私有节点预热，不能触碰真实世界。
                var sampleNodes = new[] { entity };
                new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, default(WorldTopologyDomain))
                    .Rebuild(sampleNodes);
                if (ShouldYieldResourceWork()) yield return null;
                new ElectricalNetworkGraph(default).Rebuild(sampleNodes);
                if (ShouldYieldResourceWork()) yield return null;
                new FluidNetworkGraph(static cell => cell).Rebuild(sampleNodes);
                nonEmptyGraphPrepared = true;
                if (ShouldYieldResourceWork()) yield return null;
            }

            // 不经可被 MOD 替换的玩法工厂；取消或失败也释放这份私有库存订阅。
            using (var furnace = new FurnaceLogic(entity))
            {
                if (furnace.Data.IsSmelting || furnace.IsBurning || !FurnacePreparationInventoriesAreEmpty(furnace))
                    throw new InvalidOperationException("炉体缓存预备必须使用熄火且全空的独立库存：" + source.Id);
                if (ShouldYieldResourceWork()) yield return null;
                furnace.Capture();
                if (ShouldYieldResourceWork()) yield return null;
                furnace.Tick(0f);
                _ = furnace.CalculateMaximumTemperature(0f);
                furnace.ConsumeFuel(0f);
                if (ShouldYieldResourceWork()) yield return null;
                FurnaceMaterialHeating.Process(furnace.Input, furnace.Output, TemperatureMgr.DefaultAmbientTemperature, .1f);
                if (ShouldYieldResourceWork()) yield return null;
            }
        }
    }

    #region 机器首次放置代码预编译
#if !ENABLE_IL2CPP
    private static bool machinePlacementCodePrepared;

    private IEnumerator PrewarmMachinePlacementCode()
    {
        if (machinePlacementCodePrepared) yield break;
        // 只编译游戏自有的具体方法，不执行建筑安装、事件、场景或世界访问。
        Type[] types =
        {
            typeof(BuildingPlacementService), typeof(MachineBuildingPlacementBackend),
            typeof(Mod_Building), typeof(MachineWorld), typeof(MachineEntity),
            typeof(MechanicalNetworkGraph), typeof(ElectricalNetworkGraph),
            typeof(FluidNetworkGraph), typeof(MachineCatalog), typeof(MachinePersistence),
            typeof(MachineContent), typeof(FurnaceLogic), typeof(MechanicalDepthVisual)
        };
        int prepared = 0, unsupported = 0;
        foreach (MethodBase method in ResourceRuntimeMethodPrewarmer.EnumerateClosedMethods(types))
        {
            ResourceRuntimeMethodPrewarmer.PreparationResult result = ResourceRuntimeMethodPrewarmer.PrepareMethod(method);
            if (result == ResourceRuntimeMethodPrewarmer.PreparationResult.Prepared) prepared++;
            else if (result == ResourceRuntimeMethodPrewarmer.PreparationResult.Unsupported) unsupported++;
            if (ShouldYieldResourceWork()) yield return null;
        }
        machinePlacementCodePrepared = true;
        Debug.Log($"[GameRes] 机器首次放置代码预热完成：{prepared} 个方法，跳过 {unsupported} 个不支持的方法。");
    }
#else
    private static IEnumerator PrewarmMachinePlacementCode() { yield break; }
#endif
    #endregion

    internal static FurnaceRuntimeState CreateEmptyFurnacePreparationState()
        => new()
        {
            Smelting = new ModSmeltingData
            {
                IsSmelting = false,
                InvData = new Dictionary<string, Inventory_Data>(StringComparer.Ordinal)
                {
                    ["furnace.input"] = MachineInventory.NewData("输入", 1),
                    ["furnace.output"] = MachineInventory.NewData("输出", 1),
                    ["furnace.fuel"] = MachineInventory.NewData("燃料", 1)
                }
            },
            Fuel = new FuelData { Fuel = Vector2.zero },
            AwaitingIgnition = false
        };

    private static bool FurnacePreparationInventoriesAreEmpty(FurnaceLogic furnace)
    {
        foreach (Inventory inventory in furnace.Inventories)
            foreach (ItemSlot slot in inventory.Data.itemSlots)
                if (slot?.itemData != null) return false;
        return true;
    }

    #endregion
}
