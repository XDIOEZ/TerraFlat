using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>共享气网的唯一保存载荷；不把整网气体伪装成主管段的本地库存。</summary>
[Serializable, MemoryPackable]
public partial class FluidGasNetworkSnapshot
{
    #region 权威气网快照
    public long Epoch;
    public long Sequence;
    public int OwnerId;
    public List<int> MemberIds = new();
    public double VolumeLiters;
    public double MinimumGasSpaceLiters;
    public double OccupiedLiquidLiters;
    public FluidInventoryState Inventory = new();

    public FluidGasNetworkSnapshot Clone() => new()
    {
        Epoch = Epoch, Sequence = Sequence, OwnerId = OwnerId,
        MemberIds = new List<int>(MemberIds), VolumeLiters = VolumeLiters,
        MinimumGasSpaceLiters = MinimumGasSpaceLiters, OccupiedLiquidLiters = OccupiedLiquidLiters,
        Inventory = Inventory.Clone()
    };

    public void Validate(FluidCatalog catalog = null)
    {
        if (Epoch <= 0 || Sequence < 0 || OwnerId == 0 || MemberIds == null || MemberIds.Count == 0 ||
            !MemberIds.Contains(OwnerId) || Inventory == null)
            throw new InvalidOperationException("气网快照缺少有效的代次、所有者或成员。");
        var members = new HashSet<int>();
        foreach (int id in MemberIds)
            if (id == 0 || !members.Add(id)) throw new InvalidOperationException("气网快照成员无效或重复。");
        FluidInventory.ValidateGeometry(VolumeLiters, MinimumGasSpaceLiters);
        if (!double.IsFinite(OccupiedLiquidLiters) || OccupiedLiquidLiters < 0d ||
            OccupiedLiquidLiters > VolumeLiters - MinimumGasSpaceLiters + 1e-9 * Math.Max(1d, VolumeLiters))
            throw new InvalidOperationException("气网快照的占液容积无效。");
        FluidInventory.ValidateState(Inventory, catalog ?? FluidCatalog.Default);
        foreach (FluidComponentState component in Inventory.Components)
            if (component.LiquidMoles != 0m) throw new InvalidOperationException("气网快照中不能夹带液体库存。");
    }
    #endregion
}

/// <summary>客户端只替换权威气网快照，绝不按本地节点到达顺序吸收、分配或结算气体。</summary>
public sealed class FluidGasReplicaCache
{
    #region 快照代次和唯一索引
    public sealed class Entry
    {
        public FluidGasNetworkSnapshot Snapshot { get; }
        public FluidInventory Inventory { get; }
        public double GasSpaceLiters => Math.Max(Snapshot.MinimumGasSpaceLiters,
            Snapshot.VolumeLiters - Snapshot.OccupiedLiquidLiters);
        internal Entry(FluidGasNetworkSnapshot snapshot)
        { Snapshot = snapshot.Clone(); Inventory = new FluidInventory(Snapshot.Inventory); }
    }

    private readonly Dictionary<int, Entry> owners = new();
    private readonly Dictionary<int, Entry> members = new();
    public long Epoch { get; private set; }
    public int NetworkCount => owners.Count;
    public bool TryGet(int memberId, out Entry entry) => members.TryGetValue(memberId, out entry);

    public bool AdvanceEpoch(long epoch)
    {
        if (epoch < Epoch || epoch <= 0) return false;
        if (epoch > Epoch)
        {
            owners.Clear(); members.Clear();
            Epoch = epoch;
        }
        return true;
    }

    public bool Apply(FluidGasNetworkSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Epoch < Epoch) return false;
        // 无效的新代次不得先清空仍然有效的旧快照。
        snapshot.Validate();
        if (snapshot.Epoch == Epoch && owners.TryGetValue(snapshot.OwnerId, out Entry previous) &&
            previous.Snapshot.Sequence >= snapshot.Sequence) return false;
        if (snapshot.Epoch == Epoch && owners.TryGetValue(snapshot.OwnerId, out previous))
        {
            FluidGasNetworkSnapshot previousTopology = previous.Snapshot;
            if (previousTopology.VolumeLiters != snapshot.VolumeLiters || previousTopology.MinimumGasSpaceLiters != snapshot.MinimumGasSpaceLiters ||
                previousTopology.MemberIds.Count != snapshot.MemberIds.Count)
                throw new InvalidOperationException("气网拓扑变化必须使用新的代次。");
            for (int i = 0; i < previousTopology.MemberIds.Count; i++)
                if (previousTopology.MemberIds[i] != snapshot.MemberIds[i]) throw new InvalidOperationException("气网成员变化必须使用新的代次。");
        }
        if (snapshot.Epoch == Epoch)
            foreach (int id in snapshot.MemberIds)
                if (members.TryGetValue(id, out Entry occupied) && occupied.Snapshot.OwnerId != snapshot.OwnerId)
                    throw new InvalidOperationException("同一气网代次的成员不能属于两个所有者。");
        var replacement = new Entry(snapshot);
        AdvanceEpoch(snapshot.Epoch);
        if (owners.TryGetValue(snapshot.OwnerId, out Entry old))
            foreach (int id in old.Snapshot.MemberIds)
                if (members.TryGetValue(id, out Entry current) && ReferenceEquals(current, old)) members.Remove(id);
        owners[snapshot.OwnerId] = replacement;
        foreach (int id in snapshot.MemberIds) members[id] = replacement;
        return true;
    }

    public void RemoveMember(int id)
    {
        if (!members.Remove(id, out Entry removed)) return;
        foreach (int member in removed.Snapshot.MemberIds)
            if (members.TryGetValue(member, out Entry current) && ReferenceEquals(current, removed)) return;
        owners.Remove(removed.Snapshot.OwnerId);
    }

    public void Clear()
    { owners.Clear(); members.Clear(); Epoch = 0; }
    #endregion
}
