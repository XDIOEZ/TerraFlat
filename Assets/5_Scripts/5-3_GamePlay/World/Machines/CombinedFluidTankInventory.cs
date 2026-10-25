using System;
using System.Collections.Generic;

/// <summary>组合罐共用一份运行库存，持久化时按成员容量分回独立份额。</summary>
public sealed class CombinedFluidTankInventory
{
    #region 共享库存与成员份额
    private sealed class Member
    {
        public int Id;
        public double Volume, GasCapacity, MinimumGasSpace;
        public FluidInventoryState Contents;
    }
    private readonly List<Member> members = new();
    private long savedContentRevision = -1;
    private ulong savedRandomState;
    public FluidInventory Inventory { get; }
    public CombinedFluidTankInventory(FluidCatalog catalog = null) => Inventory = new FluidInventory(catalog: catalog);
    public double VolumeLiters { get; private set; }
    public double GasCapacityStandardLiters { get; private set; }
    public double MinimumGasSpaceLiters { get; private set; }
    public int Count => members.Count;

    public bool CanCombineLiquid(FluidInventoryState contents)
    {
        string liquid = null;
        foreach (FluidComponentState component in Inventory.State.Components)
            if (component.LiquidMoles > 0m) liquid = component.FluidId;
        foreach (FluidComponentState component in contents.Components)
        {
            if (component.LiquidMoles <= 0m) continue;
            if (liquid != null && !FluidInventory.Same(liquid, component.FluidId)) return false;
            liquid = component.FluidId;
        }
        return true;
    }

    public void AddMember(int id, double volume, double gasCapacity, double minimumGasSpace, FluidInventoryState contents)
    {
        if (contents == null || !double.IsFinite(volume) || volume <= 0 ||
            !double.IsFinite(gasCapacity) || gasCapacity <= 0 || !double.IsFinite(minimumGasSpace) || minimumGasSpace <= 0 || minimumGasSpace >= volume)
            throw new ArgumentException("组合储罐成员容量无效。");
        if (members.Exists(member => member.Id == id)) throw new InvalidOperationException("组合储罐成员重复。");
        if (!CanCombineLiquid(contents)) throw new InvalidOperationException("不同液体不能合并储罐。");
        if (members.Count == 0) Inventory.State.RandomState = contents.RandomState;
        members.Add(new Member { Id = id, Volume = volume, GasCapacity = gasCapacity,
            MinimumGasSpace = minimumGasSpace, Contents = contents });
        VolumeLiters += volume; GasCapacityStandardLiters += gasCapacity; MinimumGasSpaceLiters += minimumGasSpace;
        MergeContents(contents);
        savedContentRevision = -1;
    }

    private void MergeContents(FluidInventoryState source)
    {
        foreach (FluidComponentState component in source.Components)
        {
            FluidComponentState existing = Inventory.State.Components.Find(value => FluidInventory.Same(value.FluidId, component.FluidId));
            if (existing == null) Inventory.State.Components.Add(component.Clone());
            else { existing.GasMoles += component.GasMoles; existing.LiquidMoles += component.LiquidMoles; }
        }
        Inventory.State.SynchronizeComponents();
        Inventory.State.TotalInternalEnergyJoules += source.TotalInternalEnergyJoules;
    }

    public void SynchronizeLocalShares()
    {
        long revision = Inventory.State.ContentRevision;
        if (savedContentRevision == revision && savedRandomState == Inventory.State.RandomState) return;
        // 最后一格承接十进制余数，反复组合拆分也不复制或丢失物质。
        var remaining = Inventory.State.Clone();
        decimal remainingVolume = (decimal)VolumeLiters;
        for (int index = 0; index < members.Count; index++)
        {
            Member member = members[index];
            FluidInventoryState share = member.Contents;
            bool last = index == members.Count - 1;
            decimal fraction = last ? 1m : (decimal)member.Volume / remainingVolume;
            share.CopyFrom(remaining, false);
            for (int i = 0; i < share.Components.Count; i++)
            {
                FluidComponentState current = share.Components[i], rest = remaining.Components[i];
                current.GasMoles = last ? rest.GasMoles : rest.GasMoles * fraction;
                current.LiquidMoles = last ? rest.LiquidMoles : rest.LiquidMoles * fraction;
                rest.GasMoles -= current.GasMoles; rest.LiquidMoles -= current.LiquidMoles;
            }
            share.TotalInternalEnergyJoules = last ? remaining.TotalInternalEnergyJoules : remaining.TotalInternalEnergyJoules * (double)fraction;
            remaining.TotalInternalEnergyJoules -= share.TotalInternalEnergyJoules;
            remainingVolume -= (decimal)member.Volume;
        }
        savedContentRevision = revision; savedRandomState = Inventory.State.RandomState;
    }

    public void RemoveMember(int id, bool synchronizeLocalShares = true)
    {
        int index = members.FindIndex(member => member.Id == id);
        if (index < 0) return;
        if (synchronizeLocalShares) SynchronizeLocalShares();
        Member removed = members[index]; members.RemoveAt(index);
        VolumeLiters -= removed.Volume; GasCapacityStandardLiters -= removed.GasCapacity; MinimumGasSpaceLiters -= removed.MinimumGasSpace;
        Inventory.Restore(new FluidInventoryState { RandomState = Inventory.State.RandomState });
        foreach (Member member in members) MergeContents(member.Contents);
        savedContentRevision = Inventory.State.ContentRevision;
        savedRandomState = Inventory.State.RandomState;
    }
    #endregion
}
