using System;
using System.Collections;
using System.Collections.Generic;
using FastCloner.Code;
using Newtonsoft.Json;

/// <summary>模块集合把结构变化传给宿主，数值更新仍直接使用原来的 ModuleData。</summary>
[Serializable]
public sealed class ModuleDataCollection : IDictionary<string, ModuleData>, IReadOnlyDictionary<string, ModuleData>
{
    #region 集合与宿主

    private readonly Dictionary<string, ModuleData> entries = new(StringComparer.Ordinal);
    [NonSerialized, FastClonerIgnore, JsonIgnore] private ItemData owner;
    [NonSerialized, FastClonerIgnore, JsonIgnore] private Dictionary<ModuleData, int> bindings;

    public ModuleDataCollection() { }
    public ModuleDataCollection(IEnumerable<KeyValuePair<string, ModuleData>> source)
    {
        if (source == null) return;
        foreach (var pair in source) entries.Add(pair.Key, pair.Value);
    }

    public static implicit operator ModuleDataCollection(Dictionary<string, ModuleData> source)
        => source == null ? null : new ModuleDataCollection(source);

    internal ModuleDataCollection ForOwner(ItemData nextOwner)
    {
        if (owner == null || ReferenceEquals(owner, nextOwner)) return this;
        var independent = new ModuleDataCollection();
        foreach (var pair in entries)
            independent.entries.Add(pair.Key, pair.Value == null ? null : ModuleRuntimeDataFactories.Compile(pair.Value)());
        return independent;
    }

    internal void BindOwner(ItemData nextOwner)
    {
        if (ReferenceEquals(owner, nextOwner)) return;
        if (owner != null) throw new InvalidOperationException("模块集合已绑定到另一个物品。");
        owner = nextOwner;
        bindings = new Dictionary<ModuleData, int>();
        foreach (ModuleData module in entries.Values) Attach(module);
    }

    internal void UnbindOwner(ItemData previousOwner)
    {
        if (!ReferenceEquals(owner, previousOwner)) return;
        if (bindings != null)
            foreach (ModuleData module in bindings.Keys) module.RuntimeStructureChanged -= OnModuleStructureChanged;
        bindings = null;
        owner = null;
    }

    private void Attach(ModuleData module)
    {
        if (module == null || owner == null) return;
        if (bindings.TryGetValue(module, out int count)) bindings[module] = count + 1;
        else
        {
            bindings.Add(module, 1);
            module.RuntimeStructureChanged += OnModuleStructureChanged;
        }
    }

    private void Detach(ModuleData module)
    {
        if (module == null || bindings == null || !bindings.TryGetValue(module, out int count)) return;
        if (count > 1) bindings[module] = count - 1;
        else
        {
            bindings.Remove(module);
            module.RuntimeStructureChanged -= OnModuleStructureChanged;
        }
    }

    private void OnModuleStructureChanged(ModuleData _) => owner?.NotifyModuleStructureChanged();
    private void Changed() => owner?.NotifyModuleStructureChanged();

    #endregion

    #region 字典接口

    public ModuleData this[string key]
    {
        get => entries[key];
        set
        {
            if (entries.TryGetValue(key, out ModuleData previous) && ReferenceEquals(previous, value)) return;
            Detach(previous);
            entries[key] = value;
            Attach(value);
            Changed();
        }
    }

    public ICollection<string> Keys => entries.Keys;
    public ICollection<ModuleData> Values => entries.Values;
    IEnumerable<string> IReadOnlyDictionary<string, ModuleData>.Keys => entries.Keys;
    IEnumerable<ModuleData> IReadOnlyDictionary<string, ModuleData>.Values => entries.Values;
    public int Count => entries.Count;
    public bool IsReadOnly => false;
    public bool ContainsKey(string key) => entries.ContainsKey(key);
    public bool TryGetValue(string key, out ModuleData value) => entries.TryGetValue(key, out value);

    public void Add(string key, ModuleData value)
    {
        entries.Add(key, value);
        Attach(value);
        Changed();
    }

    public bool TryAdd(string key, ModuleData value)
    {
        if (entries.ContainsKey(key)) return false;
        Add(key, value);
        return true;
    }

    public bool Remove(string key)
    {
        if (!entries.TryGetValue(key, out ModuleData value)) return false;
        entries.Remove(key);
        Detach(value);
        Changed();
        return true;
    }

    public void Clear()
    {
        if (entries.Count == 0) return;
        if (bindings != null)
            foreach (ModuleData module in bindings.Keys) module.RuntimeStructureChanged -= OnModuleStructureChanged;
        bindings?.Clear();
        entries.Clear();
        Changed();
    }

    public void Add(KeyValuePair<string, ModuleData> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<string, ModuleData> item)
        => ((ICollection<KeyValuePair<string, ModuleData>>)entries).Contains(item);
    public void CopyTo(KeyValuePair<string, ModuleData>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<string, ModuleData>>)entries).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<string, ModuleData> item) => Contains(item) && Remove(item.Key);
    public Dictionary<string, ModuleData>.Enumerator GetEnumerator() => entries.GetEnumerator();
    IEnumerator<KeyValuePair<string, ModuleData>> IEnumerable<KeyValuePair<string, ModuleData>>.GetEnumerator() => entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();

    #endregion
}
