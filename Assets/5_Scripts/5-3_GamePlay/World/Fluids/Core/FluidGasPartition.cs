using System;
using System.Collections.Generic;

/// <summary>拓扑事务先生成可验证的气体份额，确认全部可提交后才转移唯一库存所有权。</summary>
public static class FluidGasPartition
{
    #region 守恒的气相空间分配
    public static FluidInventoryState[] Prepare(FluidInventory source, IReadOnlyList<decimal> gasSpaces,
        IReadOnlyList<int> memberIds = null)
    {
        if (source == null || gasSpaces == null || gasSpaces.Count == 0 ||
            memberIds != null && memberIds.Count != gasSpaces.Count)
            throw new ArgumentException("气网拆分必须提供对应的成员和气相空间。");
        FluidInventory.ValidateState(source.State, source.Catalog);
        if (source.LiquidMoles != 0m) throw new ArgumentException("气网拆分不能搬运液体。");
        decimal totalSpace = 0m;
        foreach (decimal space in gasSpaces)
        {
            if (space <= 0m) throw new ArgumentOutOfRangeException(nameof(gasSpaces));
            totalSpace = checked(totalSpace + space);
        }
        var shares = new FluidInventoryState[gasSpaces.Count];
        for (int i = 0; i < shares.Length; i++)
            shares[i] = new FluidInventoryState { RandomState = source.State.RandomState };
        double temperature = source.GetTemperatureKelvin();
        foreach (FluidComponentState component in source.State.Components)
        {
            decimal remaining = component.GasMoles;
            FluidDefinition substance = source.Catalog.Find(component.FluidId);
            for (int i = 0; i < shares.Length; i++)
            {
                // 先算比例避免“大摩尔数 × 大容积”溢出；最后一份接回所有 decimal 尾量。
                decimal amount = i == shares.Length - 1 ? remaining :
                    Math.Min(remaining, component.GasMoles * (gasSpaces[i] / totalSpace));
                remaining -= amount;
                if (amount == 0m) continue;
                shares[i].Components.Add(new FluidComponentState { FluidId = component.FluidId, GasMoles = amount });
                shares[i].TotalInternalEnergyJoules += substance.EnergyAt(amount, 0m, temperature);
            }
        }
        // 物质量精确分配后，把浮点内能尾差交给最大非空份额，绝不静默清掉源内能。
        double allocatedEnergy = 0d;
        int energyOwner = -1;
        for (int i = 0; i < shares.Length; i++)
        {
            allocatedEnergy += shares[i].TotalInternalEnergyJoules;
            if (shares[i].Components.Count > 0 &&
                (energyOwner < 0 || shares[i].TotalInternalEnergyJoules > shares[energyOwner].TotalInternalEnergyJoules))
                energyOwner = i;
        }
        if (energyOwner >= 0)
            shares[energyOwner].TotalInternalEnergyJoules += source.State.TotalInternalEnergyJoules - allocatedEnergy;
        if (memberIds != null) AssignReservations(source, shares, memberIds);
        foreach (FluidInventoryState share in shares) FluidInventory.ValidateState(share, source.Catalog);
        return shares;
    }

    // 预留只是所有权声明。分网后按端口所属成员保留可兑现部分，多余声明释放而不改变物质量。
    private static void AssignReservations(FluidInventory source, FluidInventoryState[] shares, IReadOnlyList<int> ids)
    {
        var indexes = new Dictionary<int, int>();
        for (int i = 0; i < ids.Count; i++)
            if (!indexes.TryAdd(ids[i], i)) throw new ArgumentException("气网成员身份重复。");
        foreach (FluidOutputReservation reservation in source.State.Ports)
        {
            int separator = reservation.PortId.IndexOf(':');
            if (separator <= 0 || !int.TryParse(reservation.PortId.Substring(0, separator), out int id) ||
                !indexes.TryGetValue(id, out int index)) continue;
            FluidInventoryState share = shares[index];
            FluidComponentState component = share.Components.Find(value => FluidInventory.Same(value.FluidId, reservation.FluidId));
            if (component == null) continue;
            decimal claimed = 0m;
            foreach (FluidOutputReservation previous in share.Ports)
                if (FluidInventory.Same(previous.FluidId, reservation.FluidId)) claimed += previous.GasMoles;
            decimal amount = Math.Min(reservation.GasMoles, Math.Max(0m, component.GasMoles - claimed));
            if (amount > 0m) share.Ports.Add(new FluidOutputReservation
            { PortId = reservation.PortId, FluidId = reservation.FluidId, GasMoles = amount });
        }
    }
    #endregion
}
