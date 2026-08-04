
using FastCloner.Code;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ItemMods
{
    [NonSerialized]
    private Item _owner;

    [FastClonerIgnore]
    private Dictionary<string, Module> _mods = new Dictionary<string, Module>();

    [ShowInInspector]
    [FastClonerIgnore]
    public Dictionary<string, Module> Mods
    {
        get => _mods;
        set
        {
            _mods = value ?? new Dictionary<string, Module>();
            _owner?.MarkModuleScheduleDirty();
        }
    }

    
    [ShowInInspector]
    [FastClonerIgnore]
    public Dictionary<string, List<Module>> Mods_List { get; set; } = new();

    public ItemMods()
    {
    }

    public ItemMods(Item owner)
    {
        BindOwner(owner);
    }

    public void BindOwner(Item owner)
    {
        _owner = owner;
    }

    public List<Module> GetModList_ByID(string modID)
    {
        if (Mods_List.ContainsKey(modID) == false)
            return null;
        return Mods_List[modID];
    }
    public Module GetMod_ByID(string modID)
    {
        if (Mods_List.ContainsKey(modID) == false)
            return null;
        return Mods_List[modID][0];
    }

    public T GetMod_ByID<T>(string modID,out T mod) where T : Module
    {
        if (Mods_List.ContainsKey(modID) == false)
        {
            mod = null;
            Debug.Log("没有找到ID为" + modID + "的模块");
            return mod;
        }
        mod = Mods_List[modID][0] as T;
        return mod;
    }
    public T GetMod_ByID<T>(string modID) where T : Module
    {
        T mod;
        if (Mods_List.ContainsKey(modID) == false)
        {
            mod = null;
            Debug.LogWarning("没有找到ID为" + modID + "的模块");
            return mod;
        }
        mod = Mods_List[modID][0] as T;
        return mod;
    }

    /// <summary>
    /// Resolves a persisted module ID. Older entity prefabs can initialize a module
    /// with a shared runtime ID (for example, the generic AI ID), while their saved
    /// template identifies it by the child GameObject name. Prefer the exact ID and
    /// then fall back to that stable prefab identity.
    /// </summary>
    public Module FindModByPersistedId(string modID)
    {
        if (string.IsNullOrWhiteSpace(modID))
            return null;

        List<Module> exactMatches = GetModList_ByID(modID);
        if (exactMatches != null && exactMatches.Count > 0)
            return exactMatches[^1];

        foreach (List<Module> candidates in Mods_List.Values)
        {
            foreach (Module candidate in candidates)
            {
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.gameObject.name, modID, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        foreach (List<Module> candidates in Mods_List.Values)
        {
            foreach (Module candidate in candidates)
            {
                if (candidate != null &&
                    string.Equals(candidate.GetType().Name, modID, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public Module GetMod_ByName(string name)
    {
        return Mods[name];
    }

    public bool ContainsKey_Name(string key)
    {
        return Mods.ContainsKey(key);
    }
    public bool ContainsKey_ID(string key)
    {
        return Mods_List.ContainsKey(key);
    }


    public void AddMod(Module mod)
    {

        // 添加到 Mods
        Mods[mod._Data.Name] = mod;

        if (Mods_List.ContainsKey(mod._Data.ID) == false)
        {
            Mods_List[mod._Data.ID] = new List<Module>();
        }
        // 添加到 Mods_List
        Mods_List[mod._Data.ID].Add(mod);
        _owner?.MarkModuleScheduleDirty();
    }

    public void RemoveMod(Module mod)
    {
        // 从 Mods 中移除
        Mods.Remove(mod._Data.Name);

        // 从 Mods_List 中移除
        if (Mods_List.TryGetValue(mod._Data.ID, out var modList))
        {
            modList.Remove(mod);
            // 可选：若列表为空可移除 key
            if (modList.Count == 0)
                Mods_List.Remove(mod._Data.ID);
        }

        _owner?.MarkModuleScheduleDirty();
    }

    public bool HasMod(Module mod)
    {
        if (mod == null || string.IsNullOrEmpty(mod._Data.Name))
            return false;

        return Mods.ContainsKey(mod._Data.Name);
    }
}
