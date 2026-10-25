using System;
using System.Text;
using FlatWorld.Combat;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 环境说明与设备拆装
    public static string DescribeAtmosphere(MachineEntity node = null)
    {
        AtmosphereState atmosphere = GetCurrentAtmosphere(node);
        if (atmosphere == null) return "\n" + FlatWorldLocalizationService.GetUiText("没有大气来源");
        var text = new StringBuilder();
        if (IsShipScope)
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("环境 {0:0.##} kPa · 余量 {1:0.###} 标准 L", AtmosphereService.PressureKPa(atmosphere), atmosphere.CurrentStandardLiters));
        else text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("大气 {0:0.##} kPa · 余量 {1:0.###}/{2:0.###} 标准 L", AtmosphereService.PressureKPa(atmosphere), atmosphere.CurrentStandardLiters, atmosphere.CapacityStandardLiters));
        foreach (AtmosphereGasSnapshot gas in AtmosphereService.GetComposition(atmosphere))
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("{0} {1:0.###} 标准 L · 概率 {2:0.##}%", FlatWorldLocalizationService.GetUiText(FluidCatalog.Default.Find(gas.FluidId).DisplayName), gas.StandardLiters, gas.Fraction * 100));
        return text.ToString();
    }
    private static float GetFluidElectricalDemand(MachineEntity node)
    {
        if (!IsFluidActive(node) || node.Definition.Fluid == null || GetFluidState(node).Ruptured) return 0;
        return GetFluidDeviceBehavior(node).ElectricalDemand(node);
    }
    public static bool CanDismantleFluid(MachineEntity node, out string reason)
    {
        reason = null;
        return node == null || node.Definition.Fluid == null || Contains(node);
    }

    // 返还物携带本机完整库存；交付失败时原节点仍是唯一物料所有者。
    public static void PrepareDismantledFluidSnapshot(MachineEntity node, ItemData snapshot)
    {
        if (node?.Definition.Fluid == null || snapshot == null) return;
        MachinePersistence.Write(snapshot, "fluid", CreatePersistentFluidState(node));
    }
    public static bool TryApplyEnvironmentalDamage(MachineEntity node, float amount)
    {
        if (!GameNetwork.HasStateAuthority || !Contains(node) || !float.IsFinite(amount) || amount <= 0) return false;
        if (FlatWorld.Spaceflight.SpaceSession.Current?.Owns(node.Id) == true)
            return FlatWorld.Spaceflight.SpaceSession.Current.ApplyPieceDamage(node.Id, amount, "环境损伤");
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
        Mod_Building.DestroyMechanical(node);
        return true;
    }

    #endregion
}
