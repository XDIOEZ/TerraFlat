using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameRes
{
    #region 机器快照与炉体缓存预备

    /// <summary>进入世界前只预备私有数据和空库存，不放置设施或推进真实燃烧。</summary>
    private IEnumerator PrewarmMachineResources()
    {
        if (preparingInPlaceReload) yield break;

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
