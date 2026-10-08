using System;
using System.Collections.Generic;
using System.Text;
using FlatWorld.Combat;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 探针测压与真实动力断边
    public static double GetPressureProbeReading(MachineEntity probe)
    {
        MachineEntity pipe = fluidGraph?.PipeAt(probe.Cell);
        return pipe == null ? 0 : GetFluidInventory(pipe).GetPressureKPa(GetFluidVolumeLiters(pipe), GetFluidMinimumGasSpaceLiters(pipe));
    }
    private static void UpdatePressureProbes()
    {
        foreach (MachineEntity node in nodes.Values)
        {
            string kind = node.Definition.Fluid?.Kind;
            if (kind != "mechanical-probe" && kind != "electronic-probe" || !IsFluidActive(node)) continue;
            FluidMachineState state = GetFluidState(node);
            double pressure = GetPressureProbeReading(node);
            bool connected = state.ProbeConnected;
            if (connected && pressure > state.DisconnectPressureKPa) connected = false;
            else if (!connected && pressure <= state.ReconnectPressureKPa) connected = true;
            node.FluidProbeConnected = connected;
            if (connected == state.ProbeConnected) continue;
            state.ProbeConnected = connected; state.Status = connected ? "探针已接通" : "管压超限，探针已断开";
            dirty = true; StateChanged(node); CellChanged?.Invoke(node.Cell);
        }
    }
    public static string DescribeAtmosphere()
    {
        AtmosphereState atmosphere = GetCurrentAtmosphere();
        if (atmosphere == null) return "\n" + FlatWorldLocalizationService.GetUiText("没有大气来源");
        var text = new StringBuilder();
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("大气 {0:0.##} kPa · 余量 {1:0.###}/{2:0.###} 标准 L", AtmosphereService.PressureKPa(atmosphere), atmosphere.CurrentStandardLiters, atmosphere.CapacityStandardLiters));
        foreach (AtmosphereGasSnapshot gas in AtmosphereService.GetComposition(atmosphere))
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("{0} {1:0.###} 标准 L · 概率 {2:0.##}%", FlatWorldLocalizationService.GetUiText(FluidCatalog.Default.Find(gas.FluidId).DisplayName), gas.StandardLiters, gas.Fraction * 100));
        return text.ToString();
    }
    private static float GetFluidElectricalDemand(MachineEntity node)
    {
        if (!IsFluidActive(node) || node.Definition.Fluid == null || GetFluidState(node).Ruptured) return 0;
        FluidMachineDefinition definition = node.Definition.Fluid;
        if (definition.Kind == "compressor") return GetFluidInventory(node).GasMoles > 0 ? 1 : 0;
        if (definition.Kind == "liquid-pump") return GetFluidInventory(node).GetLiquidLiters() < definition.VolumeLiters - definition.MinimumGasSpaceLiters ? 1 : 0;
        foreach (FluidMachineReactionDefinition reaction in definition.Reactions)
            if (GetReactionInputExtent(node, reaction, 1) > 0 && GetReactionOutputExtent(node, reaction) > 0) return 1;
        return 0;
    }
    #endregion

    #region 同一血量与有限组合爆炸
    private static void AdvanceFluidTankPressure(float seconds)
    {
        var broken = new List<MachineEntity>();
        foreach (FluidTankGroup group in fluidTankGroups)
        {
            if (!group.Active || group.Members.Count == 0) continue;
            FluidPhaseChangeResult phase = FluidThermodynamics.AdvancePhaseChange(group.Inventory, group.TotalVolume, group.MinimumGasSpace, seconds,
                group.Members[0].Definition.Fluid.MaximumPhaseMolesPerSecond, false, false);
            foreach (MachineEntity member in group.Members) UpdateFluidPhaseWarning(member, "main", phase);
            if (phase.ConvertedMoles > 0) StateChanged(group.Members[0]);
            double pressure = group.Inventory.GetPressureKPa(group.TotalVolume, group.MinimumGasSpace);
            if (pressure <= group.MaxSafePressure || !string.IsNullOrEmpty(GetFluidState(group.Members[0]).RuptureBudgetId))
            { group.OverpressureVictimId = 0; continue; }
            MachineEntity victim = null;
            foreach (MachineEntity member in group.Members)
                if (member.Id == group.OverpressureVictimId && member.State?.Hp > 0) victim = member;
            if (victim == null)
            {
                var alive = new List<MachineEntity>();
                foreach (MachineEntity member in group.Members) if (member.State?.Hp > 0) alive.Add(member);
                if (alive.Count == 0) continue;
                decimal roll = FluidRandom.Next01(ref group.RandomState);
                victim = alive[Math.Min(alive.Count - 1, (int)(roll * alive.Count))]; group.OverpressureVictimId = victim.Id;
            }
            float damage = (float)(victim.Definition.Fluid.OverpressureDamagePerSecond * seconds);
            victim.State.Hp = Mathf.Max(0, victim.State.Hp - damage);
            Mod_Building.SetMechanicalDamageState(victim.Snapshot, victim.State.Hp < ResolveMaximumHp(victim.Snapshot) * .5f);
            StateChanged(victim);
            if (victim.State.Hp <= 0) broken.Add(victim);
        }
        foreach (MachineEntity node in broken) HandleFluidTankZeroHp(node);
    }

    public static bool CanDismantleFluid(MachineEntity node, out string reason)
    {
        reason = null;
        if (node?.Definition.Fluid == null) return true;
        FluidMachineState state = GetFluidState(node);
        if (!string.IsNullOrEmpty(state.RuptureBudgetId)) { reason = "正在结算罐体破裂，请等待爆炸结束"; return false; }
        if (fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) && !group.Inventory.IsEmpty)
        { reason = "请先排空整个组合储罐的气体和液体"; return false; }
        foreach (string chamber in state.Chambers.Keys)
            if (!GetFluidInventory(node, chamber).IsEmpty) { reason = "请先排空这台设备或管段的气体和液体"; return false; }
        return true;
    }

    /// <summary>压力爆炸沿真实机器生命与物理防御结算，只有归零才再入破裂队列。</summary>
    public static bool TryApplyEnvironmentalDamage(MachineEntity node, float amount)
    {
        if (!GameNetwork.HasStateAuthority || !Contains(node) || !float.IsFinite(amount) || amount <= 0) return false;
        WakeForInteraction(node);
        if (node.State == null || node.State.Hp <= 0) return false;
        if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition)) return false;
        float actual = CombatRules.ResolvePhysical(amount, definition.Health?.Defense?.Physical ?? 0, 1, 1);
        node.State.Hp = Mathf.Max(0, node.State.Hp - actual);
        if (node.State.Hp > 0)
        {
            Mod_Building.SetMechanicalDamageState(node.Snapshot, node.State.Hp < ResolveMaximumHp(node.Snapshot) * .5f);
            StateChanged(node); return actual > 0;
        }
        if (!HandleFluidTankZeroHp(node)) Mod_Building.DestroyMechanical(node);
        return true;
    }

    public static bool HandleFluidTankZeroHp(MachineEntity node)
    {
        if (!GameNetwork.HasStateAuthority || node?.Definition.Fluid?.Kind != "tank" || !Contains(node)) return false;
        FluidMachineState state = GetFluidState(node);
        if (state.Ruptured) return true;
        if (node.State != null && node.State.Hp > 0) return false;
        if (!fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group))
        { RebuildGraphsIfDirty(); fluidTankOwners.TryGetValue(node.Id, out group); }
        if (group == null) throw new InvalidOperationException("气罐破裂前缺少组合库存。");
        List<FluidBatch> gas = new(), liquid = new();
        FluidTankRuptureBudget budget;
        if (!string.IsNullOrEmpty(state.RuptureBudgetId) && owner.Mechanical.FluidRuptures.TryGetValue(state.RuptureBudgetId, out budget)) { }
        else
        {
            double pressure = group.Inventory.GetPressureKPa(group.TotalVolume, group.MinimumGasSpace);
            double gasSpace = group.Inventory.GasMoles > 0 ? Math.Max(group.MinimumGasSpace, group.TotalVolume - group.Inventory.GetLiquidLiters()) : 0;
            double positive = Math.Max(0, pressure - AtmosphereService.PressureKPa(GetCurrentAtmosphere()));
            budget = new FluidTankRuptureBudget
            { BatchId = worldKey + ":" + Guid.NewGuid().ToString("N"), PressureKPa = pressure, WorldKey = worldKey };
            foreach (MachineEntity member in group.Members)
            {
                budget.Intensities[member.Id] = positive * gasSpace * member.Definition.Fluid.VolumeLiters / group.TotalVolume;
                GetFluidState(member).RuptureBudgetId = budget.BatchId;
            }
            owner.Mechanical.FluidRuptures[budget.BatchId] = budget;
            foreach (FluidBatch batch in group.Inventory.Drain())
            {
                FluidDefinition definition = FluidCatalog.Default.Find(batch.FluidId);
                double temperature = BatchTemperature(definition, batch);
                if (batch.GasMoles > 0) gas.Add(FluidInventory.CreateBatch(definition, batch.GasMoles, 0, temperature));
                if (batch.LiquidMoles > 0) liquid.Add(FluidInventory.CreateBatch(definition, 0, batch.LiquidMoles, temperature));
            }
            group.OverpressureVictimId = 0; SaveFluidGroups();
            foreach (MachineEntity member in group.Members) CaptureFluidState(member);
        }
        state.Ruptured = true;
        if (budget.Claimed.Add(node.Id))
            PressureExplosionQueue.Enqueue(worldKey, node.Snapshot.transform.position, budget.Intensities[node.Id],
                budget.BatchId + ":" + node.Id, gas, liquid);
        CaptureFluidState(node);
        Remove(node.Id);
        return true;
    }

    public static void CompleteFluidRuptures()
    {
        if (owner?.Mechanical?.FluidRuptures == null) return;
        var completed = new List<string>();
        foreach (var pair in owner.Mechanical.FluidRuptures)
            if (pair.Value.WorldKey == worldKey) completed.Add(pair.Key);
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null) continue;
            FluidMachineState state = GetFluidState(node);
            if (!completed.Contains(state.RuptureBudgetId)) continue;
            state.RuptureBudgetId = ""; state.OverpressureVictimId = 0; CaptureFluidState(node);
        }
        foreach (string id in completed) owner.Mechanical.FluidRuptures.Remove(id);
        if (completed.Count > 0) dirty = true;
    }
    #endregion
}
