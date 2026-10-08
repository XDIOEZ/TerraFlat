using System;
using System.Collections.Generic;

public readonly struct ContainerPortFactoryContext
{
    public readonly Item Owner;
    public readonly Module Module;
    public readonly Inventory Inventory;
    public readonly ILiquidVessel Vessel;
    public readonly ContainerPortConfiguration Configuration;
    public readonly Func<bool> IsValid;
    public readonly Action OnCommit;
    public ContainerPortFactoryContext(Item owner, Module module, Inventory inventory, ILiquidVessel vessel,
        ContainerPortConfiguration configuration, Func<bool> isValid, Action onCommit = null)
    { Owner = owner; Module = module; Inventory = inventory; Vessel = vessel; Configuration = configuration; IsValid = isValid; OnCommit = onCommit; }
}

/// <summary>候选目录隔离工厂注册，发布或失败释放都不会污染仍运行的正式注册。</summary>
public static class ContainerPortFactoryRegistry
{
    #region 原生端口工厂
    internal sealed class Entry
    { public Guid Token; public Func<ContainerPortFactoryContext, IContainerPort> Factory; public Action<ContainerPortConfiguration> Validate; }
    private static Dictionary<string, Entry> published = new(StringComparer.Ordinal);
    private static CandidateScope candidate;
    public static long Generation { get; private set; } = 1;
    public static bool HasType(string id) => id is "core:inventory" or "core:liquid_vessel" or "core:world_liquid" || Current.ContainsKey(id ?? string.Empty);
    private static Dictionary<string, Entry> Current => candidate?.Entries ?? published;
    public static IDisposable Register(string typeId, Func<ContainerPortFactoryContext, IContainerPort> factory,
        Action<ContainerPortConfiguration> validate)
    {
        if (string.IsNullOrWhiteSpace(typeId) || !typeId.Contains(":") || typeId.StartsWith("core:", StringComparison.Ordinal) || factory == null || validate == null)
            throw new ArgumentException("扩展端口需要 MOD 命名空间、工厂和无副作用的配置校验。");
        var entries = Current;
        if (entries.ContainsKey(typeId) && (candidate == null || candidate.Registered.Contains(typeId)))
            throw new InvalidOperationException($"端口类型已注册：{typeId}");
        var entry = new Entry { Token = Guid.NewGuid(), Factory = factory, Validate = validate };
        entries[typeId] = entry; candidate?.Registered.Add(typeId);
        if (candidate == null) Generation++;
        return new Lease(typeId, entry.Token, entries);
    }
    public static IContainerPort Create(string typeId, ContainerPortFactoryContext context)
    {
        IContainerPort port;
        if (typeId == "core:inventory")
        {
            MachineEntity machine = context.Inventory?.MachineOwner;
            port = new InventoryItemTransferPort(context.Inventory, context.Owner, context.Configuration, context.IsValid,
                machine != null ? $"machine:{machine.Id}" : null,
                machine != null ? (UnityEngine.Vector2)machine.Position : UnityEngine.Vector2.zero,
                machine != null ? UnityEngine.SceneManagement.SceneManager.GetSceneByName(MachineWorld.WorldKey).handle : 0,
                context.OnCommit ?? (context.Module != null ? context.Module.Save : null));
        }
        else if (typeId == "core:liquid_vessel") port = new LiquidVesselTransferPort(context.Vessel, context.Configuration, context.IsValid);
        else
        {
            if (!published.TryGetValue(typeId, out Entry entry)) throw new InvalidOperationException($"端口类型未发布：{typeId}");
            port = entry.Factory(context);
        }
        if (port == null || !port.SupportsAtomicTransfer || port.Reference.PortId != context.Configuration.Id)
            throw new InvalidOperationException($"端口工厂 {typeId} 没有返回具有稳定身份和原子提交能力的端口。");
        return port;
    }
    public static void ValidateConfiguration(ContainerPortConfiguration configuration)
    {
        if (!HasType(configuration.Type)) throw new InvalidOperationException($"端口类型不存在：{configuration.Type}");
        if (Current.TryGetValue(configuration.Type, out Entry entry)) entry.Validate(configuration);
    }
    public static CandidateScope BeginCandidate()
    {
        if (candidate != null) throw new InvalidOperationException("容器端口候选目录已经开启。");
        return candidate = new CandidateScope(new Dictionary<string, Entry>(published, StringComparer.Ordinal));
    }
    public sealed class CandidateScope : IDisposable
    {
        private readonly Dictionary<string, Entry> entries;
        internal HashSet<string> Registered { get; } = new(StringComparer.Ordinal);
        private bool committed;
        internal Dictionary<string, Entry> Entries => entries;
        internal CandidateScope(Dictionary<string, Entry> entries) { this.entries = entries; }
        public void Publish()
        {
            if (!ReferenceEquals(candidate, this)) throw new InvalidOperationException("不是当前容器端口候选目录。");
            published = entries; committed = true; candidate = null; Generation++;
        }
        public void Dispose()
        { if (ReferenceEquals(candidate, this)) candidate = null; if (!committed) foreach (string id in Registered) entries.Remove(id); }
    }
    private sealed class Lease : IDisposable
    {
        private readonly string id; private readonly Guid token; private readonly Dictionary<string, Entry> entries;
        public Lease(string id, Guid token, Dictionary<string, Entry> entries) { this.id = id; this.token = token; this.entries = entries; }
        public void Dispose()
        {
            if (entries.TryGetValue(id, out Entry stored) && stored.Token == token) entries.Remove(id);
            if (published.TryGetValue(id, out Entry current) && current.Token == token) { published.Remove(id); Generation++; }
            if (candidate != null && candidate.Entries.TryGetValue(id, out Entry proposed) && proposed.Token == token) candidate.Entries.Remove(id);
        }
    }
    #endregion
}
