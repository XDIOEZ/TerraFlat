using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        private sealed partial class ShipMassReader
        {
            #region 载荷源增量缓存与实际身份归属
            private sealed class MassSource
            {
                public bool Dirty = true, Used;
                public double Mass;
                public ItemData Data;
                public Item Live;
                public MachineEntity Machine;
                public string ColdPayload;
                public object State;
                public object Logic, Processor;
                public FluidMachineState MachineFluid;
                public Inventory_Data Portable, Filter, Residue, Input, Output;
                public readonly List<Inventory_Data> MachineInventories = new();
                public readonly Dictionary<string, FluidInventoryState> Chambers = new(StringComparer.Ordinal);
                public readonly Dictionary<string, (Module Module, bool Enabled, bool Loaded)> LiveModules = new(StringComparer.Ordinal);
                public readonly HashSet<object> Dependencies = new();
                public readonly List<Action> Unsubscribe = new();
                public readonly List<Inventory_Data> Inventories = new();
                public readonly List<FluidInventoryState> Fluids = new();
                public readonly HashSet<object> Owned = new();
                public readonly HashSet<int> OwnedIds = new();
                public readonly HashSet<int> PreviousIds = new();
                public bool Released;
                public void Unbind()
                {
                    foreach (Action unsubscribe in Unsubscribe) unsubscribe();
                    Unsubscribe.Clear(); Dependencies.Clear(); Inventories.Clear(); Fluids.Clear();
                }
            }
            private readonly Dictionary<object, MassSource> cachedSources = new();
            private readonly Dictionary<object, MassSource> sourceOwners = new();
            private readonly Dictionary<int, MassSource> idOwners = new();
            private readonly HashSet<ItemData> structuralItems = new();
            private readonly HashSet<int> structuralIds = new();
            private readonly List<object> unusedSources = new();
            private MassSource activeSource;
            private bool ownershipDirty;

            public void BeginIncrementalStep()
            {
                Reset();
                foreach (MassSource source in cachedSources.Values)
                {
                    source.Used = false;
                    // 兼容直接增删列表；正常数值变化由事件在修改处标脏。
                    foreach (Inventory_Data inventory in source.Inventories) _ = inventory.MassRevision;
                    foreach (FluidInventoryState fluid in source.Fluids) _ = fluid.MassRevision;
                }
                if (ownershipDirty)
                {
                    foreach (MassSource source in cachedSources.Values) { source.Dirty = true; ReleaseOwnership(source); }
                    sourceOwners.Clear(); idOwners.Clear();
                    ownershipDirty = false;
                }
                foreach (MassSource source in cachedSources.Values) if (source.Dirty) ReleaseOwnership(source);
            }
            public bool EndIncrementalStep()
            {
                unusedSources.Clear();
                foreach (var pair in cachedSources) if (!pair.Value.Used) unusedSources.Add(pair.Key);
                bool released = false;
                foreach (object key in unusedSources)
                {
                    MassSource source = cachedSources[key]; released |= source.Owned.Count != 0 || source.OwnedIds.Count != 0;
                    ReleaseOwnership(source); source.Unbind(); cachedSources.Remove(key);
                }
                if (released) InvalidateOwnership();
                return released || ownershipDirty;
            }
            public void RegisterCachedStructuralItem(ItemData data)
            {
                if (data == null) return;
                structuralItems.Add(data); if (data.Guid != 0) structuralIds.Add(data.Guid);
            }
            private void InvalidateOwnership() => ownershipDirty = true;
            private void ReleaseOwnership(MassSource source)
            {
                if (!source.Released)
                { source.PreviousIds.Clear(); source.PreviousIds.UnionWith(source.OwnedIds); source.Released = true; }
                foreach (object value in source.Owned) if (sourceOwners.TryGetValue(value, out MassSource owner) && ReferenceEquals(owner, source)) sourceOwners.Remove(value);
                foreach (int id in source.OwnedIds) if (idOwners.TryGetValue(id, out MassSource owner) && ReferenceEquals(owner, source)) idOwners.Remove(id);
                source.Owned.Clear(); source.OwnedIds.Clear();
            }
            private bool Own(object value)
            {
                if (activeSource == null) return true;
                if (sourceOwners.TryGetValue(value, out MassSource owner)) return ReferenceEquals(owner, activeSource);
                sourceOwners.Add(value, activeSource); activeSource.Owned.Add(value); return true;
            }
            private bool OwnId(int id)
            {
                if (activeSource == null || id == 0) return true;
                if (structuralIds.Contains(id)) return false;
                if (idOwners.TryGetValue(id, out MassSource owner)) return ReferenceEquals(owner, activeSource);
                idOwners.Add(id, activeSource); activeSource.OwnedIds.Add(id); return true;
            }
            private MassSource BeginSource(object key, ItemData data = null, Item live = null, MachineEntity machine = null)
            {
                if (!cachedSources.TryGetValue(key, out MassSource source)) cachedSources.Add(key, source = new MassSource());
                source.Used = true;
                string coldPayload = machine == null ? data?.ItemSpecialData : null;
                if (!ReferenceEquals(source.Data, data) || !ReferenceEquals(source.Live, live) || !ReferenceEquals(source.Machine, machine) || source.ColdPayload != coldPayload)
                { source.Dirty = true; source.Data = data; source.Live = live; source.Machine = machine; source.ColdPayload = coldPayload; }
                if (machine != null && !MachineContextMatches(source, machine))
                { source.Dirty = true; CaptureMachineContext(source, machine); }
                if (!LiveContextMatches(source, live))
                {
                    source.Dirty = true; source.LiveModules.Clear();
                    if (live != null) foreach (var entry in live.Mods)
                        source.LiveModules.Add(entry.Key, (entry.Value, entry.Value?.Enabled == true, entry.Value?.IsRuntimeLoaded == true));
                }
                if (source.Dirty)
                { ReleaseOwnership(source); source.Unbind(); ResetVisited(); activeSource = source; }
                return source;
            }
            private static bool LiveContextMatches(MassSource source, Item live)
            {
                if (live == null) return source.LiveModules.Count == 0;
                if (source.LiveModules.Count != live.Mods.Count) return false;
                foreach (var entry in live.Mods)
                    if (!source.LiveModules.TryGetValue(entry.Key, out var previous) || !ReferenceEquals(previous.Module, entry.Value) ||
                        previous.Enabled != (entry.Value?.Enabled == true) || previous.Loaded != (entry.Value?.IsRuntimeLoaded == true)) return false;
                return true;
            }
            private static bool MachineContextMatches(MassSource source, MachineEntity machine)
            {
                if (!ReferenceEquals(source.Logic, machine.Logic) || !ReferenceEquals(source.Processor, machine.Processor) ||
                    !ReferenceEquals(source.Input, machine.Processor?.Input?.Data) || !ReferenceEquals(source.Output, machine.Processor?.Output?.Data)) return false;
                int count = machine.Logic?.Inventories.Count ?? 0;
                if (source.MachineInventories.Count != count) return false;
                for (int i = 0; i < count; i++) if (!ReferenceEquals(source.MachineInventories[i], machine.Logic.Inventories[i]?.Data)) return false;
                FluidMachineState fluid = machine.Definition.Fluid != null ? MachineWorld.GetFluidState(machine) : null;
                if (!ReferenceEquals(source.MachineFluid, fluid)) return false;
                if (fluid == null) return true;
                if (!ReferenceEquals(source.Portable, fluid.PortableTank) || !ReferenceEquals(source.Filter, fluid.FilterMaterial) ||
                    !ReferenceEquals(source.Residue, fluid.Residue) || source.Chambers.Count != fluid.Chambers.Count) return false;
                foreach (var entry in fluid.Chambers)
                    if (!source.Chambers.TryGetValue(entry.Key, out FluidInventoryState previous) || !ReferenceEquals(previous, entry.Value)) return false;
                return true;
            }
            private static void CaptureMachineContext(MassSource source, MachineEntity machine)
            {
                source.Logic = machine.Logic; source.Processor = machine.Processor;
                source.Input = machine.Processor?.Input?.Data; source.Output = machine.Processor?.Output?.Data;
                source.MachineInventories.Clear();
                if (machine.Logic != null)
                    for (int i = 0; i < machine.Logic.Inventories.Count; i++) source.MachineInventories.Add(machine.Logic.Inventories[i]?.Data);
                FluidMachineState fluid = machine.Definition.Fluid != null ? MachineWorld.GetFluidState(machine) : null;
                source.MachineFluid = fluid; source.Portable = fluid?.PortableTank; source.Filter = fluid?.FilterMaterial; source.Residue = fluid?.Residue;
                source.Chambers.Clear();
                if (fluid != null) foreach (var entry in fluid.Chambers) source.Chambers.Add(entry.Key, entry.Value);
            }
            private double EndSource(MassSource source, double mass)
            {
                if (!source.PreviousIds.SetEquals(source.OwnedIds)) InvalidateOwnership();
                source.Released = false; source.Mass = mass; source.Dirty = false; activeSource = null; return mass;
            }

            public double ReadCachedFluid(object key, FluidInventoryState state, bool gasOnly = false)
            {
                MassSource source = BeginSource(key);
                if (!ReferenceEquals(source.State, state))
                {
                    source.State = state; source.Dirty = true;
                    ReleaseOwnership(source); source.Unbind(); ResetVisited(); activeSource = source;
                }
                if (!source.Dirty) return source.Mass;
                if (!gasOnly) return EndSource(source, ReadFluidMass(state));
                WatchFluid(state);
                double mass = 0d;
                if (state?.Components != null && fluids.Add(state) && Own(state))
                    foreach (FluidComponentState component in state.Components)
                        mass += (double)component.GasMoles * FluidCatalog.Default.Find(component.FluidId).MolarMassKgPerMol;
                return EndSource(source, mass);
            }
            public double ReadCachedItem(object key, ItemData data, Item live)
            {
                MassSource source = BeginSource(key, data, live);
                return source.Dirty ? EndSource(source, ReadItem(data, live, 0)) : source.Mass;
            }
            public double ReadCachedPiece(object key, ItemData data, Item live, MachineEntity machine)
            {
                MassSource source = BeginSource(key, data, live, machine);
                if (!source.Dirty) return source.Mass;
                double mass = ReadItemContents(data, live, machine, 0);
                if (machine?.Definition.Fluid != null)
                {
                    FluidMachineState fluids = MachineWorld.GetFluidState(machine);
                    foreach (string chamber in fluids.Chambers.Keys) mass += ReadFluidMass(MachineWorld.GetFluidInventory(machine, chamber)?.State);
                    mass += ReadFluidMass(MachineWorld.GetFluidInventory(machine, "main")?.State);
                    mass += ReadInventory(fluids.PortableTank, 0) + ReadInventory(fluids.FilterMaterial, 0) + ReadInventory(fluids.Residue, 0);
                }
                return EndSource(source, mass);
            }
            private void WatchItem(ItemData data)
            { if (activeSource != null) WatchItemActive(data); }
            private void WatchItemActive(ItemData data)
            {
                MassSource source = activeSource;
                if (data == null || source == null || !source.Dependencies.Add(data)) return;
                _ = data.Stack;
                Action<ItemData> mass = _ => source.Dirty = true;
                Action<ItemData> structure = _ => { source.Dirty = true; InvalidateOwnership(); };
                data.MassChanged += mass; data.ModuleStructureChanged += structure;
                source.Unsubscribe.Add(() => { data.MassChanged -= mass; data.ModuleStructureChanged -= structure; });
            }
            private void WatchInventory(Inventory_Data inventory)
            { if (activeSource != null) WatchInventoryActive(inventory); }
            private void WatchInventoryActive(Inventory_Data inventory)
            {
                MassSource source = activeSource;
                if (inventory == null || source == null || !source.Dependencies.Add(inventory)) return;
                _ = inventory.MassRevision;
                source.Inventories.Add(inventory);
                Action<Inventory_Data> mass = _ => source.Dirty = true;
                Action<Inventory_Data> structure = _ => { source.Dirty = true; InvalidateOwnership(); };
                inventory.MassChanged += mass; inventory.RuntimeStructureChanged += structure;
                source.Unsubscribe.Add(() => { inventory.MassChanged -= mass; inventory.RuntimeStructureChanged -= structure; });
            }
            private void WatchFluid(FluidInventoryState state)
            { if (activeSource != null) WatchFluidActive(state); }
            private void WatchFluidActive(FluidInventoryState state)
            {
                MassSource source = activeSource;
                if (state == null || source == null || !source.Dependencies.Add(state)) return;
                _ = state.MassRevision; source.Fluids.Add(state);
                Action<FluidInventoryState> handler = _ => source.Dirty = true;
                state.MassChanged += handler; source.Unsubscribe.Add(() => state.MassChanged -= handler);
            }
            private void WatchLiquid(LiquidContainerState state)
            { if (activeSource != null) WatchLiquidActive(state); }
            private void WatchLiquidActive(LiquidContainerState state)
            {
                MassSource source = activeSource;
                if (state == null || source == null || !source.Dependencies.Add(state)) return;
                Action<LiquidContainerState> handler = _ => source.Dirty = true;
                state.MassChanged += handler; source.Unsubscribe.Add(() => state.MassChanged -= handler);
            }
            private void WatchPayload(ModuleData module)
            { if (activeSource != null) WatchPayloadActive(module); }
            private void WatchPayloadActive(ModuleData module)
            {
                MassSource source = activeSource;
                if (module == null || source == null || !source.Dependencies.Add(module)) return;
                Action<ModuleData> handler = _ => source.Dirty = true;
                module.PayloadChanged += handler; source.Unsubscribe.Add(() => module.PayloadChanged -= handler);
            }
            private void ClearSources()
            {
                InvalidateRuntimeSources(); structuralItems.Clear(); structuralIds.Clear();
            }
            public void InvalidateRuntimeSources()
            {
                foreach (MassSource source in cachedSources.Values) source.Unbind();
                cachedSources.Clear(); sourceOwners.Clear(); idOwners.Clear();
                activeSource = null; ownershipDirty = false;
            }
            #endregion
        }
    }
}
