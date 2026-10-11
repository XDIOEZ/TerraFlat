using System;
using System.Collections.Generic;
using MemoryPack;

public enum FluidPhase : byte { Gas = 0, Liquid = 1 }

/// <summary>唯一物质量真值；温度、压力和比例都从这里推导。</summary>
[Serializable, MemoryPackable]
public partial class FluidComponentState
{
    #region 两相物质量
    private string fluidId;
    private decimal gasMoles, liquidMoles;
    [NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    internal FluidInventoryState Owner;
    public string FluidId { get => fluidId; set { if (fluidId == value) return; fluidId = value; Owner?.ComponentChanged(); } }
    public decimal GasMoles { get => gasMoles; set { if (gasMoles == value) return; gasMoles = value; Owner?.ComponentChanged(); } }
    public decimal LiquidMoles { get => liquidMoles; set { if (liquidMoles == value) return; liquidMoles = value; Owner?.ComponentChanged(); } }
    public FluidComponentState Clone() => new() { FluidId = FluidId, GasMoles = GasMoles, LiquidMoles = LiquidMoles };
    #endregion
}

/// <summary>每个出口独占预留的单种批次，预留内容仍属于原库存。</summary>
[Serializable, MemoryPackable]
public partial class FluidOutputReservation
{
    #region 输出预留
    private string portId, fluidId;
    private decimal gasMoles, liquidMoles;
    [NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    internal FluidInventoryState Owner;
    public string PortId { get => portId; set { if (portId == value) return; portId = value; Owner?.ReservationChanged(); } }
    public string FluidId { get => fluidId; set { if (fluidId == value) return; fluidId = value; Owner?.ReservationChanged(); } }
    public decimal GasMoles { get => gasMoles; set { if (gasMoles == value) return; gasMoles = value; Owner?.ReservationChanged(); } }
    public decimal LiquidMoles { get => liquidMoles; set { if (liquidMoles == value) return; liquidMoles = value; Owner?.ReservationChanged(); } }
    [MemoryPackIgnore] public decimal TotalMoles => GasMoles + LiquidMoles;
    public FluidOutputReservation Clone() => new()
    { PortId = PortId, FluidId = FluidId, GasMoles = GasMoles, LiquidMoles = LiquidMoles };
    #endregion
}

/// <summary>管段、气罐和设备腔共用的可保存状态，不保存冲突的派生读数。</summary>
[Serializable, MemoryPackable]
public partial class FluidInventoryState
{
    #region 流体持久化
    private List<FluidComponentState> components = new();
    private double totalInternalEnergyJoules;
    public List<FluidComponentState> Components
    {
        get => components;
        set { if (ReferenceEquals(components, value)) return; components = value; SynchronizeComponents(); }
    }
    public double TotalInternalEnergyJoules
    {
        get => totalInternalEnergyJoules;
        set { if (totalInternalEnergyJoules == value) return; totalInternalEnergyJoules = value; contentRevision++; stateRevision++; }
    }
    private ulong randomState = 0x9E3779B97F4A7C15UL;
    public ulong RandomState { get => randomState; set { if (randomState == value) return; randomState = value; stateRevision++; } }
    private List<FluidOutputReservation> ports = new();
    public List<FluidOutputReservation> Ports
    {
        get => ports;
        set { if (ReferenceEquals(ports, value)) return; ports = value; SynchronizeReservations(); }
    }
    public FluidInventoryState Clone()
    {
        var result = new FluidInventoryState();
        result.CopyFrom(this);
        return result;
    }
    #endregion

    #region 内容版本与复用快照
    [NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    private List<FluidComponentState> attachedComponents = new();
    [NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    private List<FluidOutputReservation> attachedReservations = new();
    [NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    private long massRevision, contentRevision, stateRevision;
    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore] public long MassRevision { get { SynchronizeComponents(); return massRevision; } }
    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore] public long ContentRevision { get { SynchronizeComponents(); return contentRevision; } }
    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore] public long StateRevision { get { SynchronizeComponents(); SynchronizeReservations(); return stateRevision; } }
    [field: NonSerialized, MemoryPackIgnore, Newtonsoft.Json.JsonIgnore, FastCloner.Code.FastClonerIgnore]
    public event Action<FluidInventoryState> MassChanged;

    // 组分原位改量也发布变化，热量和出口预留不会让整船重新称重。
    internal void ComponentChanged()
    { massRevision++; contentRevision++; stateRevision++; MassChanged?.Invoke(this); }
    internal void ReservationChanged() => stateRevision++;

    internal void SynchronizeReservations()
    {
        attachedReservations ??= new();
        int count = ports?.Count ?? 0;
        bool changed = count != attachedReservations.Count;
        if (!changed)
            for (int i = 0; i < count; i++)
                if (!ReferenceEquals(ports[i], attachedReservations[i])) { changed = true; break; }
        if (!changed) return;
        foreach (FluidOutputReservation old in attachedReservations)
            if (old != null && ReferenceEquals(old.Owner, this)) old.Owner = null;
        attachedReservations.Clear();
        if (ports != null)
            foreach (FluidOutputReservation reservation in ports)
            { attachedReservations.Add(reservation); if (reservation != null) reservation.Owner = this; }
        ReservationChanged();
    }

    internal void SynchronizeComponents()
    {
        attachedComponents ??= new();
        int count = components?.Count ?? 0;
        bool changed = count != attachedComponents.Count;
        if (!changed)
            for (int i = 0; i < count; i++)
                if (!ReferenceEquals(components[i], attachedComponents[i])) { changed = true; break; }
        if (!changed) return;
        foreach (FluidComponentState old in attachedComponents)
            if (old != null && ReferenceEquals(old.Owner, this)) old.Owner = null;
        attachedComponents.Clear();
        if (components != null)
            foreach (FluidComponentState component in components)
            { attachedComponents.Add(component); if (component != null) component.Owner = this; }
        ComponentChanged();
    }

    public void CopyFrom(FluidInventoryState source, bool includeReservations = true)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (ReferenceEquals(source, this)) return;
        int common = Math.Min(Components.Count, source.Components.Count);
        for (int i = 0; i < common; i++)
        {
            FluidComponentState current = Components[i], original = source.Components[i];
            current.FluidId = original.FluidId; current.GasMoles = original.GasMoles; current.LiquidMoles = original.LiquidMoles;
        }
        for (int i = common; i < source.Components.Count; i++) Components.Add(source.Components[i].Clone());
        if (Components.Count > source.Components.Count) Components.RemoveRange(source.Components.Count, Components.Count - source.Components.Count);
        SynchronizeComponents();
        TotalInternalEnergyJoules = source.TotalInternalEnergyJoules; RandomState = source.RandomState;
        int portCount = includeReservations ? source.Ports.Count : 0;
        common = Math.Min(Ports.Count, portCount);
        for (int i = 0; i < common; i++)
        {
            FluidOutputReservation current = Ports[i], original = source.Ports[i];
            current.PortId = original.PortId; current.FluidId = original.FluidId;
            current.GasMoles = original.GasMoles; current.LiquidMoles = original.LiquidMoles;
        }
        for (int i = common; i < portCount; i++) Ports.Add(source.Ports[i].Clone());
        if (Ports.Count > portCount) Ports.RemoveRange(portCount, Ports.Count - portCount);
        SynchronizeReservations();
    }
    #endregion
}

/// <summary>实际转移的单种物质及其两相内能，离开来源后不再引用原库存。</summary>
public readonly struct FluidBatch
{
    #region 真实转移批次
    public FluidBatch(string fluidId, decimal gasMoles, decimal liquidMoles, double internalEnergyJoules)
    { FluidId = fluidId; GasMoles = gasMoles; LiquidMoles = liquidMoles; InternalEnergyJoules = internalEnergyJoules; }
    public string FluidId { get; }
    public decimal GasMoles { get; }
    public decimal LiquidMoles { get; }
    public double InternalEnergyJoules { get; }
    public decimal TotalMoles => GasMoles + LiquidMoles;
    public bool IsEmpty => TotalMoles <= 0m;
    public bool IsGas => GasMoles > 0m && LiquidMoles == 0m;
    public bool IsLiquid => LiquidMoles > 0m && GasMoles == 0m;
    public decimal Moles => TotalMoles;
    public double InternalEnergy => InternalEnergyJoules;
    public FluidBatch Scale(decimal fraction) => new(FluidId, GasMoles * fraction, LiquidMoles * fraction,
        InternalEnergyJoules * (double)fraction);
    #endregion
}

/// <summary>随机状态随实例保存，打开界面和失败重试不会产生新的抽签。</summary>
public static class FluidRandom
{
    #region 确定性抽签
    public static decimal Next01(ref ulong state)
    {
        if (state == 0) state = 0x9E3779B97F4A7C15UL;
        state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
        ulong value = unchecked(state * 2685821657736338717UL);
        return (decimal)(value >> 11) / 9007199254740992m;
    }
    #endregion
}
