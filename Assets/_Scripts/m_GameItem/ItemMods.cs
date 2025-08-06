
using FastCloner.Code;
using Sirenix.OdinInspector;
using System.Collections.Generic;

public class ItemMods
{
    [ShowInInspector]
    [FastClonerIgnore]
    public Dictionary<string, Module> Mods { get; set; } = new Dictionary<string, Module>();

    [FastClonerIgnore]
    [ShowInInspector]
    public Dictionary<string, List<Module>> Mods_List { get; set; } = new();

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

        // ��ӵ� Mods
        Mods[mod._Data.Name] = mod;

        if (Mods_List.ContainsKey(mod._Data.ID) == false)
        {
            Mods_List[mod._Data.ID] = new List<Module>();
        }
        // ��ӵ� Mods_List
        Mods_List[mod._Data.ID].Add(mod);
    }

    public void RemoveMod(Module mod)
    {
        if (mod == null || string.IsNullOrEmpty(mod._Data.Name) || string.IsNullOrEmpty(mod._Data.ID))
            return;

        // �� Mods ���Ƴ�
        Mods.Remove(mod._Data.Name);

        // �� Mods_List ���Ƴ�
        if (Mods_List.TryGetValue(mod._Data.ID, out var modList))
        {
            modList.Remove(mod);
            // ��ѡ�����б�Ϊ�տ��Ƴ� key
            if (modList.Count == 0)
                Mods_List.Remove(mod._Data.ID);
        }
    }

    public bool HasMod(Module mod)
    {
        if (mod == null || string.IsNullOrEmpty(mod._Data.Name))
            return false;

        return Mods.ContainsKey(mod._Data.Name);
    }
}
