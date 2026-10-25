using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Structures
{
    /// <summary>固定结构的资源只通过机器、电池和库存的正式状态接口初始化。</summary>
    public static class FixedStructureResourceInitializer
    {
        #region 冷快照资源初始化
        public static void Prepare(ItemData data, FixedStructureResources resources)
        {
            if (data == null || GameRes.Instance == null || !GameRes.Instance.TryGetItemDefinition(data.IDName, out _))
                throw new InvalidDataException("固定结构引用了不存在的物品。");
            if (resources == null) return;
            MachineDefinition definition = MachineCatalog.Get(data.IDName);
            if (definition == null) throw new InvalidDataException("固定结构资源必须配置在机器上：" + data.IDName);
            if (resources.BatteryChargeRatio.HasValue)
            {
                ValidateBattery(definition, resources.BatteryChargeRatio.Value);
                MachineState state = MachineWorld.ReadMachineState(data);
                state.ElectricalStoredJoules = (float)(definition.Electrical.CapacityJoules * resources.BatteryChargeRatio.Value);
                MachineWorld.WriteMachineState(data, state);
            }
            if (HasFluid(resources))
            {
                if (definition.Fluid == null) throw new InvalidDataException("物品没有流体库存：" + data.IDName);
                FluidMachineState state = MachinePersistence.Read<FluidMachineState>(data, "fluid") ?? new FluidMachineState();
                state.Chambers["main"] = CreateIndustrialFluidState(resources.Gas, resources.Liquids, definition.Fluid);
                MachinePersistence.Write(data, "fluid", state);
            }
            ValidateInventoryDefinitions(resources);
        }

        // 工业模板按固定数量初始化；船舱继续使用下方独立的气压初始化接口。
        public static FluidInventoryState CreateIndustrialFluidState(FixedStructureGas gas, Dictionary<string, decimal> liquids, FluidMachineDefinition definition)
        {
            var inventory = new FluidInventory();
            double temperature = gas == null ? FluidUnits.ReferenceTemperatureKelvin : gas.TemperatureCelsius + 273.15d;
            if (!Positive(temperature)) throw new InvalidDataException("工业模板温度无效。");
            if (liquids != null)
                foreach (var pair in liquids)
                {
                    FluidDefinition fluid = FluidCatalog.Default.Find(pair.Key);
                    if (pair.Value <= 0m || !inventory.TryAdd(FluidInventory.CreateBatch(fluid, 0m,
                        FluidUnits.LiquidLitersToMol(fluid, pair.Value), temperature), definition.VolumeLiters + .001d, .001d, false, true))
                        throw new InvalidDataException("工业模板液体类型或容量冲突。");
                }
            if (gas != null)
            {
                if (!gas.StandardLiters.HasValue || !Finite(gas.StandardLiters.Value) || gas.StandardLiters < 0d ||
                    gas.StandardLiters > definition.GasCapacityStandardLiters || gas.Components == null || gas.Components.Count == 0)
                    throw new InvalidDataException("工业气体模板必须配置容量内的 standardLiters。");
                double fractions = 0d;
                foreach (var pair in gas.Components) { if (!Positive(pair.Value)) throw new InvalidDataException("气体比例无效。"); fractions += pair.Value; }
                foreach (var pair in gas.Components)
                {
                    decimal amount = FluidUnits.StandardLitersToMol((decimal)(gas.StandardLiters.Value * pair.Value / fractions));
                    if (amount > 0m && !inventory.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(pair.Key), amount, 0m, temperature),
                        definition.VolumeLiters + .001d, .001d)) throw new InvalidDataException("工业模板气体无法加入。");
                }
            }
            return inventory.State.Clone();
        }

        public static FluidInventoryState CreateFluidState(FixedStructureGas gas, Dictionary<string, decimal> liquids,
            double volume, double minimum, double maxPressure)
        {
            if (!Positive(volume) || !Positive(minimum) || minimum >= volume || !Positive(maxPressure))
                throw new InvalidDataException("固定结构流体容积或安全压力无效。");
            double temperature = gas == null ? FluidUnits.ReferenceTemperatureKelvin : gas.TemperatureCelsius + 273.15d;
            if (!Positive(temperature)) throw new InvalidDataException("固定结构流体温度无效。");
            var inventory = new FluidInventory();
            if (liquids != null)
                foreach (var pair in liquids)
                {
                    if (pair.Value <= 0m) throw new InvalidDataException("固定结构液体数量必须大于零：" + pair.Key);
                    FluidDefinition fluid = FluidCatalog.Default.Find(pair.Key);
                    decimal moles = FluidUnits.LiquidLitersToMol(fluid, pair.Value);
                    if (!inventory.TryAdd(FluidInventory.CreateBatch(fluid, 0m, moles, temperature), volume, minimum))
                        throw new InvalidDataException("固定结构液体超出容积：" + pair.Key);
                }
            if (gas != null)
            {
                if (!Finite(gas.PressureKPa) || gas.PressureKPa < 0d || gas.PressureKPa > maxPressure ||
                    gas.Components == null || gas.Components.Count == 0)
                    throw new InvalidDataException("固定结构气压或气体组分无效。");
                double fractions = 0d;
                foreach (var pair in gas.Components)
                {
                    FluidCatalog.Default.Find(pair.Key);
                    if (!Positive(pair.Value)) throw new InvalidDataException("固定结构气体比例必须大于零：" + pair.Key);
                    fractions += pair.Value;
                }
                if (!Positive(fractions)) throw new InvalidDataException("固定结构气体比例合计无效。");
                double total = gas.PressureKPa * (volume - inventory.GetLiquidLiters()) / (FluidUnits.GasConstant * temperature);
                if (!Finite(total) || total > (double)FluidInventory.MaximumMoles)
                    throw new InvalidDataException("固定结构气体数量超出范围。");
                if (total > 0d)
                    foreach (var pair in gas.Components)
                    {
                        decimal moles = (decimal)(total * pair.Value / fractions);
                        if (moles <= 0m || !inventory.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(pair.Key),
                            moles, 0m, temperature), volume, minimum))
                            throw new InvalidDataException("固定结构气体无法加入库存：" + pair.Key);
                    }
            }
            if (inventory.GetPressureKPa(volume, minimum) > maxPressure * (1d + 1e-10d))
                throw new InvalidDataException("固定结构流体超过容器安全压力。");
            return inventory.State.Clone();
        }
        #endregion

        #region 机器库存预填
        public static void ApplyInventories(MachineEntity node, FixedStructureResources resources)
        {
            if (resources?.Inventories == null || resources.Inventories.Count == 0) return;
            using var scope = MachineWorld.UseNodeScope(node);
            FillInventories(node, resources, true);
            MachineWorld.CaptureSnapshot(node);
            MachineWorld.StateChanged(node);
        }

        private static void ValidateInventoryDefinitions(FixedStructureResources resources)
        {
            if (resources.Inventories == null) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FixedStructureInventory target in resources.Inventories)
            {
                if (target == null || string.IsNullOrWhiteSpace(target.Name) || !names.Add(target.Name) || target.Items == null)
                    throw new InvalidDataException("固定结构库存名称为空或重复。");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (FixedStructureInventoryItem item in target.Items)
                    if (item == null || item.Amount <= 0m || item.Amount > 16777216m || !ids.Add(item.ItemId ?? "") ||
                        !GameRes.Instance.TryGetItemDefinition(item.ItemId, out _))
                        throw new InvalidDataException("固定结构库存物品不存在、重复或数量无效：" + item?.ItemId);
            }
        }

        private static void FillInventories(MachineEntity node, FixedStructureResources resources, bool strict)
        {
            if (resources?.Inventories == null || resources.Inventories.Count == 0) return;
            ValidateInventoryDefinitions(resources);
            foreach (FixedStructureInventory target in resources.Inventories)
            {
                Inventory inventory = null;
                if (node?.Logic != null)
                    foreach (Inventory candidate in node.Logic.Inventories)
                        if (candidate?.Data?.Name == target.Name) { inventory = candidate; break; }
                if (inventory == null) throw new InvalidDataException("机器没有指定库存：" + target.Name);
                foreach (FixedStructureInventoryItem item in target.Items)
                {
                    float existing = 0f;
                    foreach (ItemSlot slot in inventory.Data.itemSlots)
                        if (slot?.itemData?.IDName == item.ItemId) existing += slot.itemData.Stack.Amount;
                    float requested = Math.Max(0f, (float)item.Amount - existing);
                    if (requested <= 0f) continue;
                    ItemData data = GameRes.Instance.CreateItemData(item.ItemId);
                    if (data.Stack == null) throw new InvalidDataException("库存物品缺少数量配置：" + item.ItemId);
                    data.Stack.Amount = requested;
                    var sourceSlot = new ItemSlot { itemData = data };
                    bool accepts = false;
                    foreach (ItemSlot slot in inventory.Data.itemSlots)
                        if (slot != null && !slot.IsFull && (slot.itemData == null || slot.itemData.CanStackWith(data)) &&
                            inventory.CanAcceptQuickTransfer(sourceSlot, slot))
                        { accepts = true; break; }
                    // 模板补给也遵守库存的标签与领域接收条件。
                    if (!accepts)
                    {
                        if (strict) throw new InvalidDataException("固定结构库存不接收此物品或没有可用槽：" + target.Name + " / " + item.ItemId);
                        continue;
                    }
                    bool canAdd = inventory.Data.TryAddItem(data, false, out float allowed);
                    if (strict && (!canAdd || allowed + .0001f < requested))
                        throw new InvalidDataException("固定结构库存容量不足：" + target.Name);
                    if (!canAdd) continue;
                    data.Stack.Amount = Math.Min(requested, allowed);
                    bool added = inventory.Data.TryAddItem(data, true, out float actual);
                    if (strict && (!added || actual + .0001f < requested))
                        throw new InvalidDataException("固定结构库存物品加入失败：" + target.Name);
                }
            }
            node.Logic?.Capture();
        }
        #endregion

        #region 模板资源补给
        public static void ValidateSharedResources(List<MachineWorld.FluidTankGroup> groups,
            Dictionary<int, FixedStructureResources> resources)
        {
            foreach (MachineWorld.FluidTankGroup group in groups)
                foreach (MachineEntity member in group.Members)
                    if (resources.TryGetValue(member.Id, out FixedStructureResources value) && HasFluid(value))
                        CreateIndustrialFluidState(value.Gas, value.Liquids, member.Definition.Fluid);
        }

        public static void Refill(MachineEntity node, FixedStructureResources resources, HashSet<FluidInventory> filled)
        {
            if (resources == null) return;
            if (node == null || filled == null) throw new ArgumentNullException(node == null ? nameof(node) : nameof(filled));
            using var scope = MachineWorld.UseNodeScope(node);
            if (resources.BatteryChargeRatio.HasValue)
            {
                ValidateBattery(node.Definition, resources.BatteryChargeRatio.Value);
                node.ElectricalStoredJoules = (float)(node.Definition.Electrical.CapacityJoules * resources.BatteryChargeRatio.Value);
                if (node.State != null) node.State.ElectricalStoredJoules = node.ElectricalStoredJoules;
            }
            if (HasFluid(resources))
            {
                if (node.Definition.Fluid == null) throw new InvalidDataException("机器没有流体库存。");
                FluidInventory inventory = MachineWorld.GetFluidInventory(node);
                if (filled.Add(inventory))
                {
                    inventory.Restore(CreateIndustrialFluidState(resources.Gas, resources.Liquids, node.Definition.Fluid));
                }
            }
            FillInventories(node, resources, false);
            MachineWorld.CaptureSnapshot(node);
            MachineWorld.StateChanged(node);
        }

        private static void ValidateBattery(MachineDefinition definition, double ratio)
        {
            if (definition?.Electrical?.IsBattery != true || !Finite(ratio) || ratio < 0d || ratio > 1d)
                throw new InvalidDataException("固定结构电池比例无效或物品不是电池。");
        }

        private static bool HasFluid(FixedStructureResources resources)
            => resources != null && (resources.Gas != null || resources.Liquids?.Count > 0);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Positive(double value) => Finite(value) && value > 0d;
        #endregion
    }
}
