using FastCloner.Code;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

[Serializable]
public sealed class ItemMods
{
    #region 只读索引与版本

    [NonSerialized] private Item owner;
    [FastClonerIgnore] private readonly Dictionary<string, Module> modules = new(StringComparer.Ordinal);
    [FastClonerIgnore] private readonly Dictionary<string, IReadOnlyList<Module>> groups = new(StringComparer.Ordinal);
    [FastClonerIgnore] private readonly Dictionary<Module, (string Name, string Id)> identities = new();
    [FastClonerIgnore] private readonly Dictionary<Type, object> capabilities = new();
    [FastClonerIgnore] private readonly ReadOnlyDictionary<string, Module> modulesView;
    [FastClonerIgnore] private readonly ReadOnlyDictionary<string, IReadOnlyList<Module>> groupsView;
    [NonSerialized] private int mutationDepth;
    [NonSerialized] private bool notificationPending;

    [ShowInInspector, FastClonerIgnore]
    public IReadOnlyDictionary<string, Module> Mods => modulesView;
    [ShowInInspector, FastClonerIgnore]
    public IReadOnlyDictionary<string, IReadOnlyList<Module>> Mods_List => groupsView;
    public uint StructureVersion { get; private set; }

    private sealed class ModuleGroup : ReadOnlyCollection<Module>
    {
        public ModuleGroup() : base(new List<Module>()) { }
        public void Add(Module module) => Items.Add(module);
        public void Remove(Module module) => Items.Remove(module);
        public void Clear() => Items.Clear();
    }

    public ItemMods()
    {
        modulesView = new ReadOnlyDictionary<string, Module>(modules);
        groupsView = new ReadOnlyDictionary<string, IReadOnlyList<Module>>(groups);
    }

    public ItemMods(Item owner) : this() => BindOwner(owner);
    internal void BindOwner(Item value) => owner = value;

    // 卸载完成后清空实例引用，保留索引容量而不把可写集合交给调用方。
    internal void ResetForReuse(Item value)
    {
        owner = value;
        modules.Clear();
        foreach (ModuleGroup group in groups.Values)
            group.Clear();
        groups.Clear();
        identities.Clear();
        capabilities.Clear();
        AdvanceVersion();
        notificationPending = mutationDepth > 0;
    }

    private void AdvanceVersion()
    {
        StructureVersion++;
        if (StructureVersion == 0) StructureVersion++;
    }

    #endregion

    #region 批量结构变更

    public MutationScope BeginMutation()
    {
        mutationDepth++;
        return new MutationScope(this);
    }

    public readonly struct MutationScope : IDisposable
    {
        private readonly ItemMods target;
        internal MutationScope(ItemMods target) => this.target = target;
        public void Dispose() => target?.EndMutation();
    }

    private void EndMutation()
    {
        if (mutationDepth <= 0)
            throw new InvalidOperationException("模块结构变更作用域不能重复结束。");
        mutationDepth--;
        if (mutationDepth == 0 && notificationPending)
            NotifyChanged();
    }

    private void Changed()
    {
        AdvanceVersion();
        capabilities.Clear();
        notificationPending = true;
        if (mutationDepth == 0)
            NotifyChanged();
    }

    private void NotifyChanged()
    {
        notificationPending = false;
        owner?.MarkModuleScheduleDirty();
        owner?.NotifyRuntimeStructureChanged();
    }

    public void AddMod(Module module)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        module.EnsureRuntimeIdentity();
        string name = module.StableName;
        string id = module.ResolvedModuleId;
        if (identities.TryGetValue(module, out var previous))
        {
            if (previous.Name != name || previous.Id != id)
                throw new InvalidOperationException($"模块 {previous.Name} 已注册，修改身份前必须移除。");
            return;
        }
        if (modules.ContainsKey(name))
            throw new InvalidOperationException($"物品 {owner?.name} 存在重复 StableName：{name}。");

        modules.Add(name, module);
        if (!groups.TryGetValue(id, out IReadOnlyList<Module> existing))
        {
            existing = new ModuleGroup();
            groups.Add(id, existing);
        }
        ((ModuleGroup)existing).Add(module);
        identities.Add(module, (name, id));
        Changed();
    }

    public void RemoveMod(Module module)
    {
        if (ReferenceEquals(module, null) || !identities.TryGetValue(module, out var identity))
            return;
        // 移除使用注册时的身份，避免可变数据留下无法清除的旧索引。
        identities.Remove(module);
        modules.Remove(identity.Name);
        ModuleGroup group = (ModuleGroup)groups[identity.Id];
        group.Remove(module);
        if (group.Count == 0) groups.Remove(identity.Id);
        Changed();
    }

    public bool HasMod(Module module) => !ReferenceEquals(module, null) && identities.ContainsKey(module);
    internal bool TryGetRegisteredIdentity(Module module, out string name, out string id)
    {
        if (!ReferenceEquals(module, null) && identities.TryGetValue(module, out var identity))
        {
            name = identity.Name;
            id = identity.Id;
            return true;
        }
        name = id = null;
        return false;
    }
    public bool ContainsKey_Name(string key) => key != null && modules.ContainsKey(key);
    public bool ContainsKey_ID(string key) => key != null && groups.ContainsKey(key);

    #endregion

    #region 模块与能力查询

    public Module GetMod_ByName(string name) => modules[name];
    public IReadOnlyList<Module> GetModList_ByID(string id) =>
        id != null && groups.TryGetValue(id, out var matches) ? matches : null;
    public Module GetMod_ByID(string id)
    {
        IReadOnlyList<Module> matches = GetModList_ByID(id);
        return matches?.Count > 0 ? matches[0] : null;
    }
    public T GetMod_ByID<T>(string id) where T : Module => GetMod_ByID(id) as T;
    public T GetMod_ByID<T>(string id, out T module) where T : Module => module = GetMod_ByID<T>(id);

    public T RequireSingleModById<T>(string id) where T : Module
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("模块 ID 不能为空。", nameof(id));
        IReadOnlyList<Module> matches = GetModList_ByID(id);
        if (matches == null || matches.Count == 0)
            throw new InvalidOperationException($"物品 {owner?.name} 缺少必需模块：{id}");
        if (matches.Count != 1)
            throw new InvalidOperationException($"物品 {owner?.name} 的模块 {id} 必须唯一，实际数量：{matches.Count}");
        if (matches[0] is not T typed)
            throw new InvalidOperationException($"物品 {owner?.name} 的模块 {id} 类型应为 {typeof(T).Name}。");
        return typed;
    }

    public IReadOnlyList<T> GetCapabilities<T>() where T : class
    {
        if (capabilities.TryGetValue(typeof(T), out object cached))
            return (IReadOnlyList<T>)cached;
        var result = new List<T>();
        foreach (Module module in modules.Values)
            if (module is T capability) result.Add(capability);
        IReadOnlyList<T> view = result.AsReadOnly();
        capabilities.Add(typeof(T), view);
        return view;
    }

    public T RequireSingleCapability<T>() where T : class
    {
        IReadOnlyList<T> matches = GetCapabilities<T>();
        if (matches.Count != 1)
            throw new InvalidOperationException($"物品 {owner?.name} 的能力 {typeof(T).Name} 必须唯一，实际数量：{matches.Count}");
        return matches[0];
    }

    public Module FindModByPersistedId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        IReadOnlyList<Module> exact = GetModList_ByID(id);
        if (exact?.Count > 0) return exact[^1];
        foreach (Module candidate in modules.Values)
            if (candidate != null && candidate.MatchesPersistedId(id)) return candidate;
        return null;
    }

    #endregion
}
