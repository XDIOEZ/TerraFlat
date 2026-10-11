using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 实际嵌套库存质量读取
        private sealed partial class ShipMassReader
        {
            private readonly HashSet<int> itemIds = new();
            private readonly HashSet<ItemData> items = new();
            private readonly HashSet<Inventory_Data> inventories = new();
            private readonly HashSet<FluidInventoryState> fluids = new();
            private readonly HashSet<LiquidContainerState> liquids = new();
            private readonly HashSet<int> liveEquipmentStores = new();
            private readonly List<HashSet<string>> liveModuleWorkspaces = new();
            private ConditionalWeakTable<Ex_ModData, BuildingPayload> buildingPayloads = new();
            private ConditionalWeakTable<Ex_ModData_MemoryPackable, BinaryPayload> binaryPayloads = new();
            private ConditionalWeakTable<InventoryInstanceSnapshot, Inventory_Data> snapshotInventories = new();
            private ConditionalWeakTable<ItemData, ColdMachinePayload> machinePayloads = new();
            private GameRes resourceSource;
            private int resourceRevision = -1;

            #region 只读载荷缓存
            private sealed class BuildingPayload
            {
                public string Source;
                public Mod_Building.Building_Data State;
                public ItemData PackedItem;
            }
            private sealed class BinaryPayload
            {
                public byte[] Source;
                public Type StateType;
                public object State;
            }
            private sealed class ColdMachinePayload
            {
                public string Source;
                public MachineStorageState Storage;
                public RecipeProcessingState Workbench;
                public FurnaceRuntimeState Furnace;
                public SlotProcessingState Compost, Drying;
                public FluidMachineState Fluid;
            }

            // 缓存只属于质量读取器，源载荷变化或资源重载后重建，不写回真实库存。
            public void ClearCache()
            {
                ClearSources();
                buildingPayloads = new(); binaryPayloads = new(); snapshotInventories = new(); machinePayloads = new();
                resourceSource = GameRes.ExistingInstance;
                resourceRevision = resourceSource != null ? resourceSource.ResourceReloadVersion : 0;
                ResetVisited();
            }
            private T ReadBinary<T>(Ex_ModData_MemoryPackable binary)
            {
                WatchPayload(binary);
                BinaryPayload cache = binaryPayloads.GetValue(binary, _ => new BinaryPayload());
                if (!ReferenceEquals(cache.Source, binary.BitData) || cache.StateType != typeof(T))
                {
                    cache.State = binary.GetData<T>();
                    cache.Source = binary.BitData; cache.StateType = typeof(T);
                }
                return cache.State == null ? default : (T)cache.State;
            }
            private ItemData ReadPackedBuilding(ItemData data)
            {
                foreach (var entry in data.ModuleDataDic)
                {
                    ModuleData module = entry.Value;
                    if (module is not Ex_ModData binary || module.ID != ModText.Building) continue;
                    WatchPayload(binary);
                    BuildingPayload cache = buildingPayloads.GetValue(binary, _ => new BuildingPayload());
                    if (cache.Source != binary.BitData)
                    {
                        cache.State = binary.GetData<Mod_Building.Building_Data>();
                        cache.Source = binary.BitData;
                        cache.PackedItem = null;
                    }
                    if (string.IsNullOrWhiteSpace(cache.State?.SnapshotBase64)) return null;
                    if (cache.PackedItem == null && !ItemNetworkStateSerialization.TryDeserializeItemData(
                        Convert.FromBase64String(cache.State.SnapshotBase64), out cache.PackedItem))
                        throw new InvalidOperationException("船载建筑库存快照无法读取。");
                    return cache.PackedItem;
                }
                return null;
            }
            private ColdMachinePayload ReadMachinePayload(ItemData data)
            {
                ColdMachinePayload cache = machinePayloads.GetValue(data, _ => new ColdMachinePayload());
                if (cache.Source == data.ItemSpecialData) return cache;
                JObject document = MachinePersistence.ReadDocument(data);
                cache.Storage = MachinePersistence.ReadPayload<MachineStorageState>(document, "storage");
                cache.Workbench = MachinePersistence.ReadPayload<RecipeProcessingState>(document, "workbench");
                cache.Furnace = MachinePersistence.ReadPayload<FurnaceRuntimeState>(document, "furnace");
                cache.Compost = MachinePersistence.ReadPayload<SlotProcessingState>(document, "compost");
                cache.Drying = MachinePersistence.ReadPayload<SlotProcessingState>(document, "drying");
                cache.Fluid = MachinePersistence.ReadPayload<FluidMachineState>(document, "fluid");
                cache.Source = data.ItemSpecialData;
                return cache;
            }
            private HashSet<string> GetLiveModuleWorkspace(int depth)
            {
                while (liveModuleWorkspaces.Count <= depth) liveModuleWorkspaces.Add(new HashSet<string>(StringComparer.Ordinal));
                HashSet<string> workspace = liveModuleWorkspaces[depth];
                workspace.Clear();
                return workspace;
            }
            #endregion

            public void Reset()
            {
                GameRes resources = GameRes.ExistingInstance;
                int revision = resources != null ? resources.ResourceReloadVersion : 0;
                if (!ReferenceEquals(resourceSource, resources) || resourceRevision != revision) ClearCache();
                else ResetVisited();
            }
            private void ResetVisited()
            {
                itemIds.Clear(); items.Clear(); inventories.Clear(); fluids.Clear(); liquids.Clear(); liveEquipmentStores.Clear();
            }

            public void RegisterStructuralItem(ItemData data)
            { if (data == null) return; items.Add(data); if (data.Guid != 0) itemIds.Add(data.Guid); }
            public double ReadItem(ItemData data, Item live, int depth)
            {
                WatchItem(data);
                if (data == null || data.Stack?.Amount <= 0f || !items.Add(data) || activeSource != null && structuralItems.Contains(data) || !Own(data)) return 0d;
                if (data.Guid != 0 && (!itemIds.Add(data.Guid) || !OwnId(data.Guid))) return 0d;
                if (depth > 64) throw new InvalidOperationException("物品库存嵌套过深。");
                if (data.SharedConfiguration == null) ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data);
                return Math.Max(0d, data.Stack?.CurrentWeight ?? 0d) + ReadItemContents(data, live, null, depth + 1);
            }
            public double ReadInventory(Inventory_Data inventory, int depth)
            {
                WatchInventory(inventory);
                if (inventory?.itemSlots == null || !inventories.Add(inventory) || !Own(inventory)) return 0d;
                double mass = 0d;
                foreach (ItemSlot slot in inventory.itemSlots) mass += ReadItem(slot?.itemData, null, depth + 1);
                return mass;
            }
            private double ReadSnapshot(InventoryInstanceSnapshot snapshot, int depth)
                => snapshot == null ? 0d : ReadInventory(snapshotInventories.GetValue(snapshot, value => value.CreateColdData()), depth + 1);
            public double ReadFluidMass(FluidInventoryState state)
            {
                WatchFluid(state);
                if (state?.Components == null || !fluids.Add(state) || !Own(state)) return 0d;
                double mass = 0d;
                foreach (FluidComponentState component in state.Components)
                    mass += (double)(component.GasMoles + component.LiquidMoles) * FluidCatalog.Default.Find(component.FluidId).MolarMassKgPerMol;
                return mass;
            }
            private double ReadLiquidMass(LiquidContainerState state)
            {
                WatchLiquid(state);
                if (state == null || !liquids.Add(state) || !Own(state)) return 0d;
                MixedLiquidContents.Ensure(state);
                double mass = 0d;
                foreach (var entry in state.Composition)
                {
                    if (!FluidCatalog.Default.TryFromLiquidId(entry.Key, out FluidDefinition definition))
                        throw new InvalidOperationException("船载液体缺少真实密度定义：" + entry.Key);
                    mass += entry.Value * (double)definition.LitersPerServing * definition.LiquidDensityKgPerLiter;
                }
                return mass;
            }
            public double ReadItemContents(ItemData data, Item live, MachineEntity machine, int depth)
            {
                WatchItem(data);
                if (data == null) return 0d;
                if (depth > 64) throw new InvalidOperationException("物品库存嵌套过深。");
                double mass = 0d;
                // 冷快照无运行模块，避免每个飞船部件都新建一个空集合。
                HashSet<string> liveModules = live != null ? GetLiveModuleWorkspace(depth) : null;
                if (live != null)
                {
                    foreach (var entry in live.Mods)
                    {
                        Module module = entry.Value;
                        if (module == null || !module.Enabled || !module.IsRuntimeLoaded) continue;
                        if (module is Mod_Equipment equipment)
                        {
                            // 装备包先读运行态，跳过同一装备可能尚未刷新的冷载荷。
                            foreach (EquipmentInstance instance in equipment.EnumerateEquippedInstances())
                                if (instance is EquipmentInstance_Bag bag)
                                { if (bag.EquippedItem != null) liveEquipmentStores.Add(bag.EquippedItem.Guid); mass += ReadInventory(bag.BagData, depth + 1); }
                            mass += ReadInventory(equipment.EquipmentInventory?.Data, depth + 1);
                            liveModules.Add(entry.Key);
                        }
                        else if (module is Mod_Inventory storage)
                        {
                            if (storage.InventoryInstances != null) foreach (Inventory inventory in storage.InventoryInstances) mass += ReadInventory(inventory?.Data, depth + 1);
                            if (storage.InventoryRefDic != null) foreach (Inventory inventory in storage.InventoryRefDic.Values) mass += ReadInventory(inventory?.Data, depth + 1);
                            liveModules.Add(entry.Key);
                        }
                        else if (module is Mod_VesselContents vessel)
                        { mass += ReadInventory(vessel.Contents?.Data, depth + 1); liveModules.Add(entry.Key); }
                        else if (module is Mod_WaterVessel water)
                        { mass += ReadLiquidMass(water.Data); liveModules.Add(entry.Key); }
                        else if (module is Mod_Hand hand) mass += ReadInventory(hand.HandInventory?.Data, depth + 1);
                        else if (module is Mod_HotBar hotbar) mass += ReadInventory(hotbar.RuntimeInventory?.Data, depth + 1);
                        else if (module is Mod_HandCraftTable handcraft)
                            mass += ReadInventory(handcraft.inputInventory?.Data, depth + 1) + ReadInventory(handcraft.outputInventory?.Data, depth + 1);
                        else if (module is Mod_HandMade handmade && handmade.InventoryRefDic != null)
                            foreach (Inventory inventory in handmade.InventoryRefDic.Values) mass += ReadInventory(inventory?.Data, depth + 1);
                    }
                }
                if (data is Data_Player player && player._inventoryData != null)
                    foreach (Inventory_Data inventory in player._inventoryData.Values) mass += ReadInventory(inventory, depth + 1);
                if (machine?.Logic != null)
                    for (int i = 0; i < machine.Logic.Inventories.Count; i++) mass += ReadInventory(machine.Logic.Inventories[i]?.Data, depth + 1);
                if (machine?.Processor != null)
                    mass += ReadInventory(machine.Processor.Input?.Data, depth + 1) + ReadInventory(machine.Processor.Output?.Data, depth + 1);
                if (machine?.Logic is VesselLogic actualVessel) mass += ReadLiquidMass(actualVessel.Data);
                bool portableRead = FluidTankStorage.TryReadContents(data, out FluidInventoryState portable);
                if (portableRead) mass += ReadFluidMass(portable);

                foreach (var entry in data.ModuleDataDic)
                {
                    ModuleData module = entry.Value;
                    if (portableRead && module?.ModuleId == Mod_FluidTank.ModuleId) WatchPayload(module);
                    // 暂停能力不会凭空拿走已经装在物品内的真实载荷。
                    if (module == null || liveModules != null && liveModules.Contains(entry.Key)) continue;
                    if (module is Inventory_ModuleData storage)
                    {
                        if (machine?.Logic == null && storage.Data != null)
                            foreach (Inventory_Data inventory in storage.Data.Values) mass += ReadInventory(inventory, depth + 1);
                        continue;
                    }
                    if (module is not Ex_ModData_MemoryPackable binary || binary.BitData?.Length is not > 0) continue;
                    if (module.ModuleId == ModText.Equipment_Module)
                    {
                        Mod_EquipmentSaveData equipment = ReadBinary<Mod_EquipmentSaveData>(binary);
                        mass += ReadSnapshot(equipment?.EquipmentInventoryData, depth + 1);
                        if (equipment?.EquipmentInstances != null) foreach (List<EquipmentInstance> group in equipment.EquipmentInstances)
                            if (group != null) foreach (EquipmentInstance instance in group)
                                if (instance is EquipmentInstance_Bag bag) mass += ReadInventory(bag.BagData, depth + 1);
                    }
                    else if (module.ModuleId == ModText.Equipment_Store && !liveEquipmentStores.Contains(data.Guid))
                    {
                        List<EquipmentInstance> equipment = ReadBinary<List<EquipmentInstance>>(binary);
                        if (equipment != null) foreach (EquipmentInstance instance in equipment)
                            if (instance is EquipmentInstance_Bag bag) mass += ReadInventory(bag.BagData, depth + 1);
                    }
                    else if (module.ModuleId == Mod_VesselContents.ModuleId && machine?.Logic is not VesselLogic)
                        mass += ReadSnapshot(ReadBinary<VesselContentsState>(binary)?.Items, depth + 1);
                    else if (module.ModuleId == Mod_WaterVessel.ModuleId && machine?.Logic is not VesselLogic)
                        mass += ReadLiquidMass(ReadBinary<LiquidContainerState>(binary));
                    else if (module.ModuleId == Mod_Spacesuit.ModuleId)
                    {
                        SpacesuitState suit = ReadBinary<SpacesuitState>(binary);
                        mass += ReadSnapshot(suit?.OxygenTank, depth + 1) + ReadSnapshot(suit?.PropulsionTank, depth + 1);
                    }
                    else if (module.ModuleId == Mod_FluidTank.ModuleId && !portableRead)
                        mass += ReadFluidMass(ReadBinary<FluidTankState>(binary)?.Contents);
                    else if (module.ModuleId == Mod_Cultivator.ModuleId && machine?.Logic is not CultivatorLogic)
                    {
                        CultivatorState plots = ReadBinary<CultivatorState>(binary);
                        mass += ReadSnapshot(plots?.Seeds, depth + 1) + ReadSnapshot(plots?.Harvest, depth + 1) + ReadSnapshot(plots?.Fertilizer, depth + 1);
                    }
                }
                if (machine == null) mass += ReadColdMachine(data, depth + 1);
                ItemData packed = ReadPackedBuilding(data);
                if (packed != null)
                    if (packed.Guid == 0 || itemIds.Add(packed.Guid) && OwnId(packed.Guid)) mass += ReadItemContents(packed, null, null, depth + 1);
                return mass;
            }
            private double ReadColdMachine(ItemData data, int depth)
            {
                if (string.IsNullOrWhiteSpace(data.ItemSpecialData) || !data.ItemSpecialData.Contains(MachinePersistence.Namespace)) return 0d;
                double mass = 0d;
                ColdMachinePayload saved = ReadMachinePayload(data);
                MachineStorageState storage = saved.Storage;
                if (storage?.Inventories != null) foreach (Inventory_Data inventory in storage.Inventories) mass += ReadInventory(inventory, depth + 1);
                RecipeProcessingState workbench = saved.Workbench;
                mass += ReadInventory(workbench?.Input, depth + 1) + ReadInventory(workbench?.Output, depth + 1);
                FurnaceRuntimeState furnace = saved.Furnace;
                if (furnace?.Smelting?.InvData != null) foreach (Inventory_Data inventory in furnace.Smelting.InvData.Values) mass += ReadInventory(inventory, depth + 1);
                mass += ReadInventory(furnace?.Processing?.Input, depth + 1) + ReadInventory(furnace?.Processing?.Output, depth + 1);
                mass += ReadInventory(saved.Compost?.Inventory, depth + 1);
                mass += ReadInventory(saved.Drying?.Inventory, depth + 1);
                FluidMachineState fluid = saved.Fluid;
                if (fluid?.Chambers != null) foreach (FluidInventoryState chamber in fluid.Chambers.Values) mass += ReadFluidMass(chamber);
                mass += ReadInventory(fluid?.PortableTank, depth + 1) + ReadInventory(fluid?.FilterMaterial, depth + 1) + ReadInventory(fluid?.Residue, depth + 1);
                return mass;
            }
        }
        #endregion
    }
}
